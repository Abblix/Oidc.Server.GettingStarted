import { createContext, useContext } from 'react';

export type UserClaims = Record<string, unknown>;

// Define the shape of the BFF context
export interface BffContextProps {
    user: UserClaims | null;
    sessionError: string | null;
    fetchBff: (endpoint: string, options?: RequestInit) => Promise<Response>;
    checkSession: () => Promise<void>;
    login: () => void;
    logout: () => Promise<void>;
}

// Creating a context for BFF to share state and functions across the application. It lives apart from
// BffProvider so that Bff.tsx exports a component only, which is what React Fast Refresh needs to keep state.
export const BffContext = createContext<BffContextProps>({
    user: null,
    sessionError: null,
    fetchBff: async () => new Response(),
    checkSession: async () => {},
    login: () => {},
    logout: async () => {}
});

// Custom hook to use the BFF context easily in other components
export const useBff = (): BffContextProps => useContext(BffContext);
