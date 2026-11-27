import { Route, Routes } from 'react-router'
import { Button } from '@/components/ui/button'
import { Card, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Toaster } from '@/components/ui/sonner'
import { AppSidebar } from '@/components/AppSidebar'
import { SidebarInset, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { useSession } from '@/session'
import SitesPage from '@/pages/SitesPage'
import SitePage from '@/pages/SitePage'
import AlarmsPage from '@/pages/AlarmsPage'

function Gate({ title, text, children }: { title: string; text: string; children: React.ReactNode }) {
  return (
    <div className="flex min-h-[60vh] items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <div className="mb-2 flex items-center gap-2 font-semibold"><img src="/logo.svg" alt="" className="size-7" />SiteSense</div>
          <CardTitle>{title}</CardTitle><CardDescription>{text}</CardDescription>
        </CardHeader>
        <CardFooter>{children}</CardFooter>
      </Card>
    </div>
  )
}

export default function App() {
  const { me, loading, can, login, logout } = useSession()
  let body: React.ReactNode
  if (loading) body = <p className="p-6 text-sm text-muted-foreground">Loading…</p>
  else if (!me) body = <Gate title="Sign in" text="Log in to see your sites."><Button className="w-full" onClick={login}>Log in with Keycloak</Button></Gate>
  else if (!can.seeAggregates) body = <Gate title="No access" text="Your account cannot use this app."><Button variant="outline" onClick={logout}>Log out</Button></Gate>
  else return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-12 items-center border-b px-4"><SidebarTrigger /></header>
        <main className="p-4">
          <Routes>
            <Route path="/" element={<SitesPage />} />
            <Route path="/sites/:siteId" element={<SitePage />} />
            <Route path="/alarms" element={can.seeIndividuals ? <AlarmsPage /> : <p className="p-6">Not found.</p>} />
            <Route path="*" element={<p className="p-6">Not found.</p>} />
          </Routes>
        </main>
      </SidebarInset>
      <Toaster />
    </SidebarProvider>
  )
  return (
    <div className="min-h-screen bg-background text-foreground">
      <main className="mx-auto max-w-4xl p-4">{body}</main>
      <Toaster />
    </div>
  )
}
