import { useState, type FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { Navigation, Site } from '@/api'

export const defaultNavigation: Navigation = {
  walkingSpeedMps: 1.3, unpavedSpeedFactor: 0.8, stepsSpeedFactor: 0.5, connectToleranceM: 0.5, maxSnapDistanceM: 30,
}
const navFields: [keyof Navigation, string][] = [
  ['walkingSpeedMps', 'Walking speed (m/s)'], ['unpavedSpeedFactor', 'Unpaved speed factor'], ['stepsSpeedFactor', 'Steps speed factor'],
  ['connectToleranceM', 'Connect tolerance (m)'], ['maxSnapDistanceM', 'Max snap distance (m)'],
]

// The same form creates a site (id editable) and edits one (id fixed by the URL).
export function SiteForm({ initial, idEditable, submitLabel, onSubmit }: {
  initial: Site; idEditable: boolean; submitLabel: string; onSubmit: (s: Site) => Promise<void>
}) {
  const [s, setS] = useState(initial)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setBusy(true)
    try { await onSubmit(s) } finally { setBusy(false) }
  }
  const text = (id: string, label: string, key: 'siteId' | 'name' | 'timeZone' | 'mapFile', disabled = false) => (
    <div className="grid gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} value={s[key]} disabled={disabled} required onChange={e => setS({ ...s, [key]: e.target.value })} />
    </div>
  )
  return (
    <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2">
      {text('siteId', 'Site id', 'siteId', !idEditable)}
      {text('name', 'Name', 'name')}
      {text('timeZone', 'Time zone', 'timeZone')}
      {text('mapFile', 'Map file name', 'mapFile')}
      {navFields.map(([k, label]) => (
        <div key={k} className="grid gap-1.5">
          <Label htmlFor={k}>{label}</Label>
          <Input id={k} type="number" step="any" min="0" required value={s.navigation[k]}
            onChange={e => setS({ ...s, navigation: { ...s.navigation, [k]: Number(e.target.value) } })} />
        </div>
      ))}
      <div className="sm:col-span-2"><Button type="submit" disabled={busy}>{submitLabel}</Button></div>
    </form>
  )
}
