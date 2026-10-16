import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { clearSession, fetchMe, hasSession, onUnauthorized } from '../api/client';
import { authApi } from '../api/endpoints';
import type { Role, UserSummary } from '../api/types';

interface AuthContextValue {
  user: UserSummary | null;
  initialising: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (input: {
    email: string;
    password: string;
    confirmPassword: string;
    firstName: string;
    lastName: string;
    phoneNumber?: string;
  }) => Promise<void>;
  logout: () => void;
  hasRole: (...roles: Role[]) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserSummary | null>(null);
  const [initialising, setInitialising] = useState(true);

  const logout = useCallback(() => {
    clearSession();
    setUser(null);
  }, []);

  useEffect(() => {
    onUnauthorized(() => setUser(null));
  }, []);

  useEffect(() => {
    let active = true;
    async function hydrate() {
      if (!hasSession()) {
        setInitialising(false);
        return;
      }
      try {
        const me = await fetchMe();
        if (active) {
          setUser(me);
        }
      } catch {
        if (active) {
          clearSession();
          setUser(null);
        }
      } finally {
        if (active) {
          setInitialising(false);
        }
      }
    }
    void hydrate();
    return () => {
      active = false;
    };
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const response = await authApi.login(email, password);
    setUser(response.user);
  }, []);

  const register = useCallback(
    async (input: {
      email: string;
      password: string;
      confirmPassword: string;
      firstName: string;
      lastName: string;
      phoneNumber?: string;
    }) => {
      const response = await authApi.register(input);
      setUser(response.user);
    },
    [],
  );

  const hasRole = useCallback(
    (...roles: Role[]) => roles.some((role) => user?.roles.includes(role) ?? false),
    [user],
  );

  const value = useMemo<AuthContextValue>(
    () => ({ user, initialising, login, register, logout, hasRole }),
    [user, initialising, login, register, logout, hasRole],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider.');
  }
  return context;
}
