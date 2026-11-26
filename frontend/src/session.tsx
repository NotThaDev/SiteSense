import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setOnUnauthorized } from '@/api'

type Me = { tenantId: string; roles: string[]; username: string | null }
type Session = { me: Me | null; loading: boolean; isAdmin: boolean; login: () => void; logout: () => void }

const Ctx = createContext<Session | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setOnUnauthorized(() => setMe(null))
    api<Me>('GET', '/me').then(r => setMe(r.data)).catch(() => setMe(null)).finally(() => setLoading(false))
  }, [])

  const value: Session = {
    me, loading,
    isAdmin: me?.roles.includes('Admin') ?? false,
    login: () => { location.href = `/api/v1/auth/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}` },
    logout: () => { location.href = '/api/v1/auth/logout' },
  }
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useSession() {
  const s = useContext(Ctx)
  if (!s) throw new Error('SessionProvider missing')
  return s
}
