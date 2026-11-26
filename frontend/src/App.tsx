import { Route, Routes } from 'react-router'
import { Button } from '@/components/ui/button'
import { Card, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Toaster } from '@/components/ui/sonner'
import { AppSidebar } from '@/components/AppSidebar'
import { SidebarInset, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { useSession } from '@/session'
import SitesPage from '@/pages/SitesPage'
import SitePage from '@/pages/SitePage'

function Gate({ title, text, children }: { title: string; text: string; children: React.ReactNode }) {
  return (
    <div className="flex min-h-[60vh] items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader><CardTitle>{title}</CardTitle><CardDescription>{text}</CardDescription></CardHeader>
        <CardFooter>{children}</CardFooter>
      </Card>
    </div>
  )
}

export default function App() {
  const { me, loading, isAdmin, login, logout } = useSession()
  let body: React.ReactNode
  if (loading) body = <p className="p-6 text-sm text-muted-foreground">Loading…</p>
  else if (!me) body = <Gate title="SiteSense Admin" text="Log in to manage sites, maps and devices."><Button onClick={login}>Log in</Button></Gate>
  else if (!isAdmin) body = <Gate title="Admin role required" text="Your account cannot use this app."><Button variant="outline" onClick={logout}>Logout</Button></Gate>
  else return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-12 items-center border-b px-4"><SidebarTrigger /></header>
        <main className="p-4">
          <Routes>
            <Route path="/" element={<SitesPage />} />
            <Route path="/sites/:siteId" element={<SitePage />} />
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
