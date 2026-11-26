import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, toastError, type Site } from '@/api'
import { SiteForm, defaultNavigation } from './SiteForm'

const blank: Site = { siteId: '', name: '', timeZone: 'Europe/Rome', navigation: defaultNavigation }

export default function SitesPage() {
  const [sites, setSites] = useState<Site[] | null>(null)
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const load = useCallback(() => api<{ items: Site[] }>('GET', '/sites').then(r => setSites(r.data.items)).catch(toastError), [])
  useEffect(() => { load() }, [load])

  const create = async (s: Site) => {
    try {
      const { siteId: _, ...body } = s
      const r = await api<{ siteId: string }>('POST', '/sites', { body })
      toast.success('Site created')
      setOpen(false)
      navigate(`/sites/${r.data.siteId}`)
    } catch (e) { toastError(e) }
  }

  return (
    <div className="grid gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Sites</h1>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger asChild><Button>New site</Button></DialogTrigger>
          <DialogContent className="max-w-2xl">
            <DialogHeader><DialogTitle>New site</DialogTitle><DialogDescription>Defaults come from the example site.</DialogDescription></DialogHeader>
            <SiteForm initial={blank} submitLabel="Create site" onSubmit={create} />
          </DialogContent>
        </Dialog>
      </div>
      <Table>
        <TableHeader><TableRow><TableHead>Name</TableHead><TableHead>Time zone</TableHead><TableHead>Id</TableHead></TableRow></TableHeader>
        <TableBody>
          {sites?.map(s => (
            <TableRow key={s.siteId}>
              <TableCell><Link className="underline" to={`/sites/${s.siteId}`}>{s.name}</Link></TableCell>
              <TableCell>{s.timeZone}</TableCell>
              <TableCell className="font-mono text-xs text-muted-foreground">{s.siteId}</TableCell>
            </TableRow>
          ))}
          {sites?.length === 0 && <TableRow><TableCell colSpan={3} className="text-muted-foreground">No sites yet.</TableCell></TableRow>}
        </TableBody>
      </Table>
    </div>
  )
}
