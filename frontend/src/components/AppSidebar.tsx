import { LogOut, MapPin } from 'lucide-react'
import { Link, useLocation } from 'react-router'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Sidebar, SidebarContent, SidebarFooter, SidebarGroup, SidebarHeader, SidebarMenu, SidebarMenuButton, SidebarMenuItem,
} from '@/components/ui/sidebar'
import { useSession } from '@/session'

const initials = (name: string) => {
  const parts = name.split(/[-_. ]+/).filter(Boolean)
  return (parts.length > 1 ? parts[0][0] + parts[1][0] : name.slice(0, 2)).toUpperCase()
}

function UserMenu() {
  const { me, logout } = useSession()
  const name = me?.username ?? me?.tenantId ?? ''
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button aria-label="Account menu" className="flex w-full items-center gap-2 rounded-md p-2 text-left hover:bg-sidebar-accent">
          <Avatar><AvatarFallback>{initials(name)}</AvatarFallback></Avatar>
          <div className="grid min-w-0 gap-0.5">
            <span className="truncate text-sm font-medium">{name}</span>
            <Badge variant="secondary" className="w-fit">{me?.roles[0] ?? 'No role'}</Badge>
          </div>
        </button>
      </PopoverTrigger>
      <PopoverContent side="top" align="start" className="w-56 p-1">
        <Button variant="ghost" className="w-full justify-start" onClick={logout}><LogOut /> Log out</Button>
      </PopoverContent>
    </Popover>
  )
}

export function AppSidebar() {
  const { pathname } = useLocation()
  return (
    <Sidebar>
      <SidebarHeader>
        <Link to="/" className="flex items-center gap-2 p-2 font-semibold"><img src="/logo.svg" alt="" className="size-7" />SiteSense</Link>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton asChild isActive={pathname === '/' || pathname.startsWith('/sites')}>
                <Link to="/"><MapPin />Sites</Link>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter><UserMenu /></SidebarFooter>
    </Sidebar>
  )
}
