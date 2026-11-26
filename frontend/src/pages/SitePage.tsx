import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, api, toastError, type Device, type Site } from '@/api'
import { SiteForm } from './SiteForm'

function SettingsCard({ siteId, site, setSite }: { siteId: string; site: Site | null; setSite: (s: Site) => void }) {
  const [etag, setEtag] = useState<string | null>(null)
  const [rev, setRev] = useState(0)
  useEffect(() => {
    api<Site>('GET', `/sites/${siteId}`).then(r => { setSite(r.data); setEtag(r.etag) }).catch(toastError)
  }, [siteId, rev])
  const save = async (s: Site) => {
    try {
      await api('PUT', `/sites/${siteId}`, { body: s, etag: etag ?? undefined })
      toast.success('Site saved')
      setRev(rev + 1)
    } catch (e) { toastError(e) }
  }
  return (
    <Card>
      <CardHeader><CardTitle>Settings</CardTitle></CardHeader>
      <CardContent>
        {site && <SiteForm key={rev} initial={site} submitLabel="Save" onSubmit={save} />}
      </CardContent>
    </Card>
  )
}

function MapCard({ siteId }: { siteId: string }) {
  const [file, setFile] = useState<File | null>(null)
  const [version, setVersion] = useState<number | null>(null)
  const [errors, setErrors] = useState<{ elementId: string | null; reason: string }[]>([])
  const upload = async (e: FormEvent) => {
    e.preventDefault()
    if (!file) return
    setVersion(null); setErrors([])
    try {
      const r = await api<{ mapVersion: number }>('PUT', `/sites/${siteId}/map`, { rawBody: await file.text() })
      setVersion(r.data.mapVersion)
      toast.success('Map saved')
    } catch (err) {
      if (err instanceof ApiError && err.status === 422) setErrors(err.problem.errors ?? [])
      toastError(err)
    }
  }
  return (
    <Card>
      <CardHeader><CardTitle>Map</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        <form onSubmit={upload} className="flex flex-wrap items-end gap-2">
          <div className="grid gap-1.5">
            <Label htmlFor="mapFile">GeoJSON file</Label>
            <Input id="mapFile" type="file" accept=".geojson,.json" onChange={e => setFile(e.target.files?.[0] ?? null)} />
          </div>
          <Button type="submit" disabled={!file}>Upload map</Button>
        </form>
        {version !== null && <p className="text-sm">Map version <Badge>{version}</Badge></p>}
        {errors.length > 0 && (
          <Table>
            <TableHeader><TableRow><TableHead>Element</TableHead><TableHead>Reason</TableHead></TableRow></TableHeader>
            <TableBody>{errors.map((e, i) => <TableRow key={i}><TableCell>{e.elementId ?? '(file)'}</TableCell><TableCell>{e.reason}</TableCell></TableRow>)}</TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

function DevicesCard({ siteId }: { siteId: string }) {
  const [devices, setDevices] = useState<Device[]>([])
  const [newId, setNewId] = useState('')
  const [toDelete, setToDelete] = useState<string | null>(null)
  const load = useCallback(() => api<{ items: Device[] }>('GET', `/sites/${siteId}/devices`).then(r => setDevices(r.data.items)).catch(toastError), [siteId])
  useEffect(() => { load() }, [load])

  const register = async (e: FormEvent) => {
    e.preventDefault()
    try {
      await api('POST', `/sites/${siteId}/devices`, { body: { deviceId: newId } })
      setNewId(''); load()
    } catch (err) { toastError(err) }
  }
  const remove = async () => {
    const id = toDelete
    setToDelete(null)
    if (!id) return
    try {
      const { etag } = await api('GET', `/sites/${siteId}/devices/${id}`)
      await api('DELETE', `/sites/${siteId}/devices/${id}`, { etag: etag ?? undefined })
      load()
    } catch (err) { toastError(err) }
  }
  return (
    <Card>
      <CardHeader><CardTitle>Devices</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        <form onSubmit={register} className="flex flex-wrap items-end gap-2">
          <div className="grid gap-1.5">
            <Label htmlFor="deviceId">Device id</Label>
            <Input id="deviceId" value={newId} required onChange={e => setNewId(e.target.value)} />
          </div>
          <Button type="submit">Register</Button>
        </form>
        <Table>
          <TableHeader><TableRow><TableHead>Device id</TableHead><TableHead className="w-24" /></TableRow></TableHeader>
          <TableBody>
            {devices.map(d => (
              <TableRow key={d.deviceId}>
                <TableCell>{d.deviceId}</TableCell>
                <TableCell><Button size="sm" variant="outline" onClick={() => setToDelete(d.deviceId)}>Delete</Button></TableCell>
              </TableRow>
            ))}
            {devices.length === 0 && <TableRow><TableCell colSpan={2} className="text-muted-foreground">No devices.</TableCell></TableRow>}
          </TableBody>
        </Table>
      </CardContent>
      <Dialog open={toDelete !== null} onOpenChange={o => !o && setToDelete(null)}>
        <DialogContent>
          <DialogHeader><DialogTitle>Delete device?</DialogTitle><DialogDescription>Device {toDelete} will be unregistered from this site.</DialogDescription></DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setToDelete(null)}>Cancel</Button>
            <Button variant="destructive" onClick={remove}>Delete</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  )
}

export default function SitePage() {
  const { siteId = '' } = useParams()
  const [site, setSite] = useState<Site | null>(null)
  return (
    <div className="grid gap-4">
      <div className="flex items-baseline gap-3">
        <Link to="/" className="text-sm underline">Sites</Link>
        <div>
          {site ? <h1 className="text-xl font-semibold">{site.name}</h1> : <Skeleton className="h-7 w-48" />}
          <p className="font-mono text-xs text-muted-foreground">{siteId}</p>
        </div>
      </div>
      <SettingsCard siteId={siteId} site={site} setSite={setSite} />
      <MapCard siteId={siteId} />
      <DevicesCard siteId={siteId} />
    </div>
  )
}
