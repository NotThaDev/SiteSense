import { useEffect, useRef, useState, type RefObject } from 'react'
import L from 'leaflet'
import type { Feature, FeatureCollection, Geometry } from 'geojson'
import { GeoJSON, MapContainer, TileLayer, useMap } from 'react-leaflet'
import { Maximize2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import { ApiError, api, toastError } from '@/api'
import { useSession } from '@/session'
import { useSiteEvents, type SiteEvents, type Status } from '@/realtime'

type Props = Record<string, string | number | boolean | undefined>

const AREA_FILL: Record<string, string> = { work: '#3b82f6', rest: '#22c55e', parking: '#9ca3af', restricted: '#f97316', hazard: '#ef4444' }
const LEGEND: [string, string][] = [
  ['Perimeter', '#1f2937'], ['Work', '#3b82f6'], ['Rest', '#22c55e'], ['Parking', '#9ca3af'],
  ['Restricted', '#f97316'], ['Hazard', '#ef4444'], ['Path', '#374151'], ['Assembly point', '#16a34a'], ['Entrance', '#2563eb'],
]

function style(f?: Feature<Geometry, Props>): L.PathOptions {
  const p = f?.properties ?? {}
  // Leaflet applies this style to the circle markers too, so points get their colours here.
  if (p.kind === 'assemblyPoint') return { radius: 7, fillColor: '#16a34a', fillOpacity: 1, color: '#14532d', weight: 1 } as L.CircleMarkerOptions
  if (p.kind === 'entrance') return { radius: 6, fillColor: '#2563eb', fillOpacity: 1, color: '#ffffff', weight: 2 } as L.CircleMarkerOptions
  if (p.kind === 'path') {
    return { color: p.steps ? '#111827' : '#374151', weight: p.steps ? 4 : 3, dashArray: p.surface === 'unpaved' ? '4 4' : undefined }
  }
  const t = String(p.areaType)
  if (t === 'perimeter') return { color: '#1f2937', weight: 3, fill: false }
  if (t === 'restricted' || t === 'hazard') return { color: '#b91c1c', weight: 2, dashArray: '6 4', fillColor: AREA_FILL[t], fillOpacity: 0.35 }
  return { color: AREA_FILL[t] ?? '#6b7280', weight: 2, fillColor: AREA_FILL[t], fillOpacity: 0.25 }
}

// Popup text is set with textContent: names come from the uploaded file.
function popup(f: Feature<Geometry, Props>, count?: number): HTMLElement {
  const p = f.properties ?? {}
  const lines: (string | undefined)[] = p.kind === 'area' ? [String(p.name), String(p.areaType), p.capacity !== undefined ? `capacity ${p.capacity}` : undefined, count !== undefined ? `occupancy ${count}` : undefined]
    : p.kind === 'path' ? [String(p.id), String(p.surface), p.steps ? 'steps' : undefined, `slope ${p.slopePercent}%`, `width ${p.widthM} m`]
    : p.kind === 'assemblyPoint' ? [String(p.name), `capacity ${p.capacity}`]
    : [String(p.name)]
  const div = document.createElement('div')
  lines.filter(Boolean).forEach(l => { const d = document.createElement('div'); d.textContent = l!; div.appendChild(d) })
  return div
}

const STALE_MS = 5 * 60_000 // a device without updates for this long fades (the occupancy rule)
const PULSE_MS = 30_000
const textEl = (text: string, tag = 'span') => { const e = document.createElement(tag); e.textContent = text; return e }

// Occupancy labels and colours, device dots and alert pulses, drawn straight on the Leaflet layers (updated in place, never re-created per event).
function LiveLayers({ geo, live, individuals, now }: { geo: RefObject<L.GeoJSON | null>; live: SiteEvents; individuals: boolean; now: number }) {
  const map = useMap()
  const dots = useRef(new Map<string, L.CircleMarker>())
  const dotLayer = useRef(L.layerGroup())
  const pulseLayer = useRef(L.layerGroup())
  const centres = useRef(new Map<string, L.LatLng>())

  useEffect(() => {
    const layers = [dotLayer.current, pulseLayer.current]
    layers.forEach(l => l.addTo(map))
    return () => { layers.forEach(l => l.remove()); dots.current.clear() }
  }, [map])

  useEffect(() => {
    centres.current.clear()
    geo.current?.eachLayer(layer => {
      const f = (layer as L.Layer & { feature?: Feature<Geometry, Props> }).feature
      if (f?.properties?.kind !== 'area') return
      const path = layer as L.Polygon
      centres.current.set(String(f.properties.id), path.getBounds().getCenter())
      const count = live.occupancy.get(String(f.properties.id))
      const cap = Number(f.properties.capacity)
      const base = style(f)
      const colour = count !== undefined && cap > 0 ? (count >= cap ? '#dc2626' : count >= 0.8 * cap ? '#f59e0b' : undefined) : undefined
      path.setStyle(colour ? { ...base, color: colour, fillColor: colour, fillOpacity: 0.5 } : base)
      path.unbindTooltip()
      if (count !== undefined) path.bindTooltip(textEl(String(count)), { permanent: true, direction: 'center', className: 'occ-label' })
    })
  }, [geo, live.occupancy])

  useEffect(() => {
    if (!individuals) return
    const seen = new Set<string>()
    live.devices.forEach((d, id) => {
      seen.add(id)
      const opacity = now - d.timestampMs > STALE_MS ? 0.7 : 1
      let m = dots.current.get(id)
      if (m) m.setLatLng([d.lat, d.lon]).setStyle({ fillOpacity: opacity, opacity })
      else {
        m = L.circleMarker([d.lat, d.lon], { radius: 5, color: '#ffffff', weight: 1, fillColor: '#7c3aed', fillOpacity: opacity, opacity })
          .bindTooltip(textEl(id)).addTo(dotLayer.current)
        dots.current.set(id, m)
      }
    })
    dots.current.forEach((m, id) => { if (!seen.has(id)) { m.remove(); dots.current.delete(id) } })
  }, [live.devices, individuals, now])

  useEffect(() => {
    pulseLayer.current.clearLayers()
    if (!individuals) return
    for (const a of live.alerts) {
      if (now - a.receivedAt > PULSE_MS) continue
      const d = live.devices.get(a.deviceId)
      const at = d ? L.latLng(d.lat, d.lon) : a.areaId ? centres.current.get(a.areaId) : undefined
      if (!at) continue
      const ring = textEl('', 'span'); ring.className = 'alert-pulse'
      L.marker(at, { interactive: false, icon: L.divIcon({ className: 'alert-icon', html: ring, iconSize: [24, 24] }) }).addTo(pulseLayer.current)
    }
  }, [live.alerts, live.devices, individuals, now])
  return null
}

const CHIP: Record<Status, [string, string]> = {
  live: ['Live', 'bg-green-600 text-white'], reconnecting: ['Reconnecting…', 'bg-amber-500 text-black'], offline: ['Offline', 'bg-gray-500 text-white'],
}

function FitBounds({ data }: { data: FeatureCollection }) {
  const map = useMap()
  useEffect(() => {
    const perimeter = data.features.find(f => f.properties?.areaType === 'perimeter')
    const bounds = L.geoJSON(perimeter ?? data).getBounds()
    if (bounds.isValid()) map.fitBounds(bounds)
  }, [map, data])
  return null
}

// Leaflet measures its container once; in the dialog the size settles after the open animation, so measure again.
function ResizeAfterOpen() {
  const map = useMap()
  useEffect(() => { const t = setTimeout(() => map.invalidateSize(), 200); return () => clearTimeout(t) }, [map])
  return null
}

// The map itself, used in the card and in the large dialog.
// isolate: Leaflet's z-indexes (400–1000) stay inside the map, so popovers and dialogs draw above it.
function MapCanvas({ data, etag, live, individuals, now }: { data: FeatureCollection; etag: string; live: SiteEvents; individuals: boolean; now: number }) {
  const geo = useRef<L.GeoJSON>(null)
  const occ = useRef(live.occupancy)
  occ.current = live.occupancy
  const [label, chip] = CHIP[live.status]
  return (
    <div className="relative isolate h-full overflow-hidden rounded-xl border">
      <MapContainer center={[0, 0]} zoom={2} style={{ height: '100%', width: '100%' }}>
        <TileLayer url="https://tile.openstreetmap.org/{z}/{x}/{y}.png" maxZoom={19}
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' />
        <GeoJSON key={etag} ref={geo} data={data}
          style={f => style(f as Feature<Geometry, Props>)}
          pointToLayer={(f, latlng) => L.circleMarker(latlng, style(f as Feature<Geometry, Props>))}
          // Top-left padding keeps an opened popup clear of the zoom control.
          onEachFeature={(f, layer) => layer.bindPopup(() => popup(f as Feature<Geometry, Props>, occ.current.get(String((f.properties as Props).id))), { autoPanPaddingTopLeft: L.point(60, 60) })} />
        <LiveLayers key={etag} geo={geo} live={live} individuals={individuals} now={now} />
        <FitBounds data={data} />
        <ResizeAfterOpen />
      </MapContainer>
      <span className={`absolute top-2 left-14 rounded-full px-2 py-0.5 text-xs font-medium shadow ${chip}`} style={{ zIndex: 1000 }} role="status">{label}</span>
      <ul className="absolute bottom-2 left-2 rounded-md border bg-background/90 p-2 text-xs shadow" style={{ zIndex: 1000 }}>
        {LEGEND.map(([label, color]) => (
          <li key={label} className="flex items-center gap-1.5">
            <span className="inline-block size-2.5 rounded-sm" style={{ background: color }} />{label}
          </li>
        ))}
      </ul>
    </div>
  )
}

export default function MapView({ siteId, reloadKey }: { siteId: string; reloadKey: number }) {
  const [data, setData] = useState<FeatureCollection | null>(null)
  const [etag, setEtag] = useState('')
  const [empty, setEmpty] = useState(false)
  const [large, setLarge] = useState(false)
  const { can } = useSession()
  const live = useSiteEvents(siteId)
  // Re-evaluates the fade and the pulses without waiting for an event.
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 15_000); return () => clearInterval(t) }, [])
  useEffect(() => {
    api<FeatureCollection>('GET', `/sites/${siteId}/map`)
      .then(r => { setData(r.data); setEtag(r.etag ?? ''); setEmpty(false) })
      .catch(err => {
        if (err instanceof ApiError && err.status === 404) { setData(null); setEmpty(true) } else toastError(err)
      })
  }, [siteId, reloadKey])

  if (empty) return <p className="text-sm text-muted-foreground">{can.manage ? 'Upload a map to see it here' : 'No map for this site yet.'}</p>
  if (!data) return null
  const canvas = <MapCanvas data={data} etag={etag} live={live} individuals={can.seeIndividuals} now={now} />
  return (
    <div className="grid gap-3">
      <div className="relative" style={{ height: 420 }}>
        {canvas}
        <Button type="button" variant="outline" size="icon" onClick={() => setLarge(true)} className="absolute top-2 right-2 shadow"
          aria-label="Enlarge map" title="Enlarge map">
          <Maximize2 />
        </Button>
        <Dialog open={large} onOpenChange={setLarge}>
          <DialogContent className="flex h-[90vh] w-[95vw] max-w-[95vw] flex-col p-2 sm:max-w-[95vw]">
            <DialogTitle className="sr-only">Site map</DialogTitle>
            <div className="min-h-0 flex-1">{large && canvas}</div>
          </DialogContent>
        </Dialog>
      </div>
      {can.seeIndividuals && (
        <div>
          <h3 className="mb-1 text-sm font-medium">Live alerts</h3>
          <ul className="grid gap-1 text-sm">
            {live.alerts.map(a => (
              <li key={a.alertId} className="flex flex-wrap gap-x-3 rounded border border-red-300 bg-red-50 px-2 py-1 dark:border-red-900 dark:bg-red-950">
                <strong>{a.type}</strong><span>device {a.deviceId}</span>{a.areaId && <span>area {a.areaId}</span>}
                <span>{new Date(a.eventTimeMs).toLocaleTimeString()}</span>{a.late && <span>(late)</span>}
              </li>
            ))}
            {live.alerts.length === 0 && <li className="text-muted-foreground">No alerts.</li>}
          </ul>
        </div>
      )}
    </div>
  )
}
