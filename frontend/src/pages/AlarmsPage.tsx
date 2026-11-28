import { useEffect, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, toastError, type Site } from '@/api'
import { useTenantAlerts, type AlertState } from '@/realtime'

type Row = { alertId: string; siteId: string; type: string; eventTimeMs: number; deviceId: string | null; areaId: string | null; late: boolean; state: AlertState; endTimeMs: number | null }
type Page = { items: Row[]; nextCursor: string | null }
const PAGE = 50

// Fixed per alarm type (decided with the user); unknown types fall back to Low.
type Severity = 'Low' | 'Medium' | 'High' | 'Critical'
const SEVERITY: Record<string, Severity> = { AreaIntrusion: 'High', DeviceSilent: 'Medium', ZoneAnomaly: 'Medium', SuddenImmobility: 'Critical' }
const SEVERITY_CLASS: Record<Severity, string> = {
  Low: 'bg-secondary text-secondary-foreground',
  Medium: 'bg-yellow-500 text-black',
  High: 'bg-orange-500 text-white',
  Critical: 'bg-red-600 text-white',
}
const severityOf = (type: string): Severity => SEVERITY[type] ?? 'Low'

// Mounted per tab (keyed). Loads every site of the tenant; the site filter is applied here, in the browser.
function AlarmsTable({ siteId, state, siteNames }: { siteId: string; state: AlertState; siteNames: Map<string, string> }) {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)
  const live = useTenantAlerts()

  useEffect(() => {
    let stale = false
    api<Page>('GET', `/alerts?limit=${PAGE}&state=${state}`)
      .then(r => { if (!stale) { setRows(r.data.items); setNextCursor(r.data.nextCursor) } })
      .catch(e => { if (!stale) { setRows([]); toastError(e) } })
    return () => { stale = true }
    // History also reloads when a live update ends an alarm that was not loaded on this page.
  }, [state, state === 'NotActive' ? live.updates.size : 0])

  const loadMore = async () => {
    if (!nextCursor) return
    setLoadingMore(true)
    try {
      const r = await api<Page>('GET', `/alerts?limit=${PAGE}&state=${state}&cursor=${encodeURIComponent(nextCursor)}`)
      setRows(prev => [...(prev ?? []), ...r.data.items])
      setNextCursor(r.data.nextCursor)
    } catch (e) { toastError(e) } finally { setLoadingMore(false) }
  }

  // Live alarms not already loaded go on top; the hub's updates apply to every row.
  const loadedIds = new Set(rows?.map(r => r.alertId))
  const liveRows: Row[] = live.alerts
    .filter(a => !loadedIds.has(a.alertId))
    .map(a => ({ alertId: a.alertId, siteId: a.siteId, type: a.type, eventTimeMs: a.eventTimeMs, deviceId: a.deviceId, areaId: a.areaId, late: a.late, state: a.state, endTimeMs: null }))
  const shown = [...liveRows, ...(rows ?? [])].map(r => {
    const u = live.updates.get(r.alertId)
    return u ? { ...r, state: u.state, endTimeMs: u.endTimeMs } : r
  }).filter(r => r.state === state && (siteId === '' || r.siteId === siteId))

  return (
    <div className="grid gap-3">
      <p className="text-xs text-muted-foreground">Live: {live.status}</p>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Time</TableHead><TableHead>Site</TableHead><TableHead>Type</TableHead><TableHead>Device</TableHead>
            <TableHead>Area</TableHead><TableHead>Severity</TableHead><TableHead>Late</TableHead><TableHead>State</TableHead>
            {state === 'NotActive' && <TableHead>Ended</TableHead>}
          </TableRow>
        </TableHeader>
        <TableBody>
          {shown.map(r => (
            <TableRow key={r.alertId}>
              <TableCell>{new Date(r.eventTimeMs).toLocaleString()}</TableCell>
              <TableCell>{siteNames.get(r.siteId) ?? r.siteId}</TableCell>
              <TableCell>{r.type}</TableCell>
              <TableCell className="font-mono text-xs">{r.deviceId ?? '—'}</TableCell>
              <TableCell className="font-mono text-xs">{r.areaId ?? '—'}</TableCell>
              <TableCell><Badge className={`border-transparent ${SEVERITY_CLASS[severityOf(r.type)]}`}>{severityOf(r.type)}</Badge></TableCell>
              <TableCell>{r.late ? 'Yes' : 'No'}</TableCell>
              <TableCell>{r.state === 'Active' ? <Badge className="border-transparent bg-green-600 text-white">Active</Badge> : <Badge variant="secondary">Not active</Badge>}</TableCell>
              {state === 'NotActive' && <TableCell>{r.endTimeMs ? new Date(r.endTimeMs).toLocaleString() : '—'}</TableCell>}
            </TableRow>
          ))}
          {rows && shown.length === 0 && <TableRow><TableCell colSpan={9} className="text-muted-foreground">{state === 'Active' ? 'No active alarms.' : 'No past alarms.'}</TableCell></TableRow>}
          {rows === null && <TableRow><TableCell colSpan={9} className="text-muted-foreground">Loading…</TableCell></TableRow>}
        </TableBody>
      </Table>
      {nextCursor && <div><Button variant="outline" onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading…' : 'Load more'}</Button></div>}
    </div>
  )
}

export default function AlarmsPage() {
  const [sites, setSites] = useState<Site[] | null>(null)
  const [siteId, setSiteId] = useState('')
  const [tab, setTab] = useState<AlertState>('Active')

  useEffect(() => {
    api<{ items: Site[] }>('GET', '/sites')
      .then(r => { setSites(r.data.items) })
      .catch(toastError)
  }, [])

  return (
    <div className="grid gap-4">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Alarms</h1>
        <select
          aria-label="Site"
          className="h-9 min-w-48 rounded-md border border-input bg-transparent px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
          value={siteId}
          onChange={e => setSiteId(e.target.value)}
        >
          <option value="">All sites</option>
          {sites?.map(s => <option key={s.siteId} value={s.siteId}>{s.name}</option>)}
        </select>
      </div>
      <div className="flex gap-1" role="tablist">
        <Button role="tab" aria-selected={tab === 'Active'} variant={tab === 'Active' ? 'default' : 'outline'} onClick={() => setTab('Active')}>Active</Button>
        <Button role="tab" aria-selected={tab === 'NotActive'} variant={tab === 'NotActive' ? 'default' : 'outline'} onClick={() => setTab('NotActive')}>History</Button>
      </div>
      <AlarmsTable key={tab} siteId={siteId} state={tab} siteNames={new Map(sites?.map(s => [s.siteId, s.name]))} />
    </div>
  )
}
