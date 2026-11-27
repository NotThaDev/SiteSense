import { useEffect, useState } from 'react'
import L from 'leaflet'
import type { Feature, FeatureCollection, Geometry } from 'geojson'
import { GeoJSON, MapContainer, TileLayer, useMap } from 'react-leaflet'
import { ApiError, api, toastError } from '@/api'

type Props = Record<string, string | number | boolean | undefined>

const AREA_FILL: Record<string, string> = { work: '#3b82f6', rest: '#22c55e', parking: '#9ca3af', restricted: '#f97316', hazard: '#ef4444' }
const LEGEND: [string, string][] = [
  ['Perimeter', '#1f2937'], ['Work', '#3b82f6'], ['Rest', '#22c55e'], ['Parking', '#9ca3af'],
  ['Restricted', '#f97316'], ['Hazard', '#ef4444'], ['Path', '#374151'], ['Assembly point', '#16a34a'], ['Entrance', '#2563eb'],
]

function style(f?: Feature<Geometry, Props>): L.PathOptions {
  const p = f?.properties ?? {}
  if (p.kind === 'path') {
    return { color: p.steps ? '#111827' : '#374151', weight: p.steps ? 4 : 3, dashArray: p.surface === 'unpaved' ? '4 4' : undefined }
  }
  const t = String(p.areaType)
  if (t === 'perimeter') return { color: '#1f2937', weight: 3, fill: false }
  if (t === 'restricted' || t === 'hazard') return { color: '#b91c1c', weight: 2, dashArray: '6 4', fillColor: AREA_FILL[t], fillOpacity: 0.35 }
  return { color: AREA_FILL[t] ?? '#6b7280', weight: 2, fillColor: AREA_FILL[t], fillOpacity: 0.25 }
}

// Popup text is set with textContent: names come from the uploaded file.
function popup(f: Feature<Geometry, Props>): HTMLElement {
  const p = f.properties ?? {}
  const lines: (string | undefined)[] = p.kind === 'area' ? [String(p.name), String(p.areaType), p.capacity !== undefined ? `capacity ${p.capacity}` : undefined]
    : p.kind === 'path' ? [String(p.id), String(p.surface), p.steps ? 'steps' : undefined, `slope ${p.slopePercent}%`, `width ${p.widthM} m`]
    : p.kind === 'assemblyPoint' ? [String(p.name), `capacity ${p.capacity}`]
    : [String(p.name)]
  const div = document.createElement('div')
  lines.filter(Boolean).forEach(l => { const d = document.createElement('div'); d.textContent = l!; div.appendChild(d) })
  return div
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

export default function MapView({ siteId, reloadKey }: { siteId: string; reloadKey: number }) {
  const [data, setData] = useState<FeatureCollection | null>(null)
  const [etag, setEtag] = useState('')
  const [empty, setEmpty] = useState(false)
  useEffect(() => {
    api<FeatureCollection>('GET', `/sites/${siteId}/map`)
      .then(r => { setData(r.data); setEtag(r.etag ?? ''); setEmpty(false) })
      .catch(err => {
        if (err instanceof ApiError && err.status === 404) { setData(null); setEmpty(true) } else toastError(err)
      })
  }, [siteId, reloadKey])

  if (empty) return <p className="text-sm text-muted-foreground">Upload a map to see it here</p>
  if (!data) return null
  return (
    <div className="relative overflow-hidden rounded-xl border" style={{ height: 420 }}>
      <MapContainer center={[0, 0]} zoom={2} style={{ height: '100%', width: '100%' }}>
        <TileLayer url="https://tile.openstreetmap.org/{z}/{x}/{y}.png" maxZoom={19}
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' />
        <GeoJSON key={etag} data={data}
          style={f => style(f as Feature<Geometry, Props>)}
          pointToLayer={(f, latlng) => L.circleMarker(latlng, f.properties.kind === 'assemblyPoint'
            ? { radius: 7, fillColor: '#16a34a', fillOpacity: 1, color: '#14532d', weight: 1 }
            : { radius: 6, fillColor: '#2563eb', fillOpacity: 1, color: '#ffffff', weight: 2 })}
          onEachFeature={(f, layer) => layer.bindPopup(() => popup(f as Feature<Geometry, Props>))} />
        <FitBounds data={data} />
      </MapContainer>
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
