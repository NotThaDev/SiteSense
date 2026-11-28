import { useEffect, useState } from 'react'
import { HttpError, HubConnectionBuilder } from '@microsoft/signalr'
import { api } from '@/api'

export type Status = 'live' | 'reconnecting' | 'offline'
export type Device = { lat: number; lon: number; timestampMs: number }
// receivedAt is client time: it drives the short pulse on the map.
export type AlertState = 'Active' | 'NotActive'
export type Alert = { alertId: string; siteId: string; type: string; eventTimeMs: number; deviceId: string; areaId: string | null; late: boolean; state: AlertState; receivedAt: number }
// Last AlertUpdated per alertId, kept for the Alarms page (its rows may come from the API, not from `alerts`).
export type AlertUpdate = { state: AlertState; endTimeMs: number | null }
export type SiteEvents = { status: Status; occupancy: Map<string, number>; devices: Map<string, Device>; alerts: Alert[]; updates: Map<string, AlertUpdate> }

type Evt = { seq: number; type: string; siteId: string; atMs: number; data: any }
type OccupancyResponse = { atMs: number; areas: { areaId: string; count: number }[] }
type View = Omit<SiteEvents, 'status'>

const TYPES = ['PositionUpdated', 'OccupancyChanged', 'AlertRaised', 'AlertUpdated']
const EMPTY: View = { occupancy: new Map(), devices: new Map(), alerts: [], updates: new Map() }
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

function apply(v: View, events: Evt[]): View {
  let { occupancy, devices, alerts, updates } = v
  for (const e of events) {
    if (e.type === 'OccupancyChanged') {
      occupancy = new Map(occupancy)
      for (const a of e.data.areas ?? []) occupancy.set(a.areaId, a.count)
    } else if (e.type === 'PositionUpdated') {
      const d = e.data
      if ((devices.get(d.deviceId)?.timestampMs ?? -1) >= d.timestampMs) continue
      devices = new Map(devices).set(d.deviceId, { lat: d.lat, lon: d.lon, timestampMs: d.timestampMs })
    } else if (e.type === 'AlertRaised') {
      alerts = [{ ...e.data, siteId: e.siteId, state: 'Active', receivedAt: Date.now() } as Alert, ...alerts].slice(0, 20)
    } else if (e.type === 'AlertUpdated') {
      const d = e.data as { alertId: string } & AlertUpdate
      updates = new Map(updates).set(d.alertId, { state: d.state, endTimeMs: d.endTimeMs ?? null })
      alerts = alerts.map(a => a.alertId === d.alertId ? { ...a, state: d.state } : a)
    }
  }
  return { occupancy, devices, alerts, updates }
}

// Live events of one site: connect, JoinSite, catch up from GET /events, then follow the hub (SS-49).
export const useSiteEvents = (siteId: string): SiteEvents => useEvents(siteId)

// Alarms of every site of the tenant (JoinTenant); each alert keeps its siteId.
export const useTenantAlerts = (): SiteEvents => useEvents(null)

// siteId null = tenant mode: no site filter, only alert events.
function useEvents(siteId: string | null): SiteEvents {
  const [status, setStatus] = useState<Status>('reconnecting')
  const [view, setView] = useState<View>(EMPTY)

  useEffect(() => {
    let stopped = false
    let lastSeq = 0
    let ready = false
    const buffer: Evt[] = []
    setView(EMPTY); setStatus('reconnecting')

    // A 401 means the cookie expired: /me answers 401 too and shows the sign-in screen through the api helper.
    const refreshSession = () => { api('GET', '/me').catch(() => {}) }
    const conn = new HubConnectionBuilder()
      .withUrl('/api/v1/realtime', { headers: { 'X-CSRF': '1' } })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ctx =>
          ctx.retryReason instanceof HttpError && ctx.retryReason.statusCode === 401 ? null : ([1000, 2000, 5000][ctx.previousRetryCount] ?? 10000),
      })
      .build()

    const take = (events: Evt[]) => {
      const fresh = events.filter(e => (siteId === null ? e.type.startsWith('Alert') : e.siteId === siteId) && e.seq > lastSeq)
      if (fresh.length === 0) return
      lastSeq = Math.max(lastSeq, ...fresh.map(e => e.seq))
      setView(v => apply(v, fresh))
    }
    for (const t of TYPES) conn.on(t, (e: Evt) => { if (ready) take([e]); else buffer.push(e) })

    const sync = async () => {
      ready = false
      await (siteId === null ? conn.invoke('JoinTenant') : conn.invoke('JoinSite', siteId))
      // Snapshot first: buffered events are applied after it, so newer events win. Tenant-wide views have no site.
      if (siteId !== null) {
        try {
          const r = await api<OccupancyResponse>('GET', `/sites/${siteId}/occupancy`)
          setView(v => ({ ...v, occupancy: new Map(r.data.areas.map(a => [a.areaId, a.count] as const)) }))
        } catch { /* best effort: a failed snapshot must not break sync */ }
      }
      // Catch up only after a reconnect: from seq 0 it would replay every retained event of the tenant.
      for (let more = lastSeq > 0; more && !stopped;) {
        const r = await api<{ items: Evt[]; lastSeq: number; hasMore: boolean }>('GET', `/events?after=${lastSeq}`)
        take(r.data.items)
        lastSeq = Math.max(lastSeq, r.data.lastSeq)
        more = r.data.hasMore
      }
      // Live events that arrived during catch-up; seq <= lastSeq are dropped by take.
      take(buffer.splice(0).sort((a, b) => a.seq - b.seq))
      ready = true
      if (!stopped) setStatus('live')
    }
    const fail = (e: unknown) => {
      if (stopped) return
      setStatus('offline')
      if (e instanceof HttpError && e.statusCode === 401) refreshSession()
    }

    conn.onreconnecting(() => { ready = false; if (!stopped) setStatus('reconnecting') })
    conn.onreconnected(() => { sync().catch(fail) })
    conn.onclose(() => { if (!stopped) { setStatus('offline'); refreshSession() } })

    // start() does not retry by itself: the same capped backoff until the first connection works.
    ;(async () => {
      for (let n = 0; !stopped; n++) {
        try { await conn.start(); await sync(); return } catch (e) {
          if (stopped) return
          if (e instanceof HttpError && e.statusCode === 401) { fail(e); return }
          setStatus('reconnecting')
          await sleep([1000, 2000, 5000][n] ?? 10000)
          if (conn.state !== 'Disconnected') await conn.stop().catch(() => {})
        }
      }
    })()

    return () => {
      stopped = true
      const leave = conn.state === 'Connected' ? (siteId === null ? conn.invoke('LeaveTenant') : conn.invoke('LeaveSite', siteId)).catch(() => {}) : Promise.resolve()
      leave.finally(() => conn.stop())
    }
  }, [siteId])

  return { status, ...view }
}
