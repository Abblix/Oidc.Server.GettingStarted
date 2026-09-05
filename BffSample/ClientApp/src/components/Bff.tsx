import { createContext, useCallback, useContext, useEffect, useState, ReactNode, FC } from 'react';

type UserClaims = Record<string, unknown>;

// Define the shape of the BFF context
interface BffContextProps {
    user: UserClaims | null;
    sessionError: string | null;
    fetchBff: (endpoint: string, options?: RequestInit) => Promise<Response>;
    checkSession: () => Promise<void>;
    login: () => void;
    logout: () => void;
}

// Creating a context for BFF to share state and functions across the application
const BffContext = createContext<BffContextProps>({
    user: null,
    sessionError: null,
    fetchBff: async () => new Response(),
    checkSession: async () => {},
    login: () => {},
    logout: () => {}
});

interface BffProviderProps {
    baseUrl: string;
    children: ReactNode;
}

export const BffProvider: FC<BffProviderProps> = ({ baseUrl, children }) => {
    const [user, setUser] = useState<UserClaims | null>(null);
    const [sessionError, setSessionError] = useState<string | null>(null);

    // Normalize the base URL by removing a trailing slash to avoid inconsistent URLs
    const normalizedBaseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;

    const fetchBff = useCallback(
        async (endpoint: string, options: RequestInit = {}): Promise<Response> => {
            try {
                // The fetch function includes credentials to handle cookies, which are necessary for authentication
                return await fetch(`${normalizedBaseUrl}/${endpoint}`, {
                    credentials: 'include',
                    ...options
                });
            } catch (error) {
                console.error(`Error during ${endpoint} call:`, error);
                throw error;
            }
        },
        [normalizedBaseUrl]
    );

    // The login function redirects to the login page when user needs to authenticate
    const login = useCallback((): void => {
        window.location.replace(`${normalizedBaseUrl}/login`);
    }, [normalizedBaseUrl]);

    // The checkSession function is responsible for verifying the user session on initial render
    const checkSession = useCallback(async (): Promise<void> => {
        let response: Response;
        try {
            response = await fetchBff('check_session');
        } catch (cause) {
            // The BFF not running, a rejected certificate: anything that stops the request from
            // being answered at all.
            console.error('Session check failed:', cause);
            setSessionError('the BFF could not be reached - is it running?');
            return;
        }

        try {
            if (response.ok) {
                // If the session is valid, update the user state with the received claims data
                setUser(await response.json());
                setSessionError(null);
            } else if (response.status === 401) {
                // If the user is not authenticated, redirect him to the login page
                login();
            } else {
                console.error('Unexpected response from checking session:', response);
                setSessionError(`the BFF answered ${response.status}`);
            }
        } catch (cause) {
            // A 200 whose body is not JSON lands here, and it is a different fault from an
            // unreachable BFF: say so rather than blaming the process that did answer.
            console.error('Session response could not be read:', cause);
            setSessionError('the BFF answered something that is not JSON');
        }
    }, [fetchBff, login]);

    // Full RP-initiated logout: navigate the browser to /bff/logout so it can follow the
    // redirect to the provider's end-session endpoint and back. A fetch cannot navigate the page.
    const logout = useCallback((): void => {
        window.location.href = `${normalizedBaseUrl}/logout`;
    }, [normalizedBaseUrl]);

    // Run the session check once on mount. checkSession sets state only after an awaited
    // fetch, so this is not the synchronous render cascade the set-state-in-effect rule guards.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    useEffect(() => { checkSession(); }, [checkSession]);

    return (
        // Providing the BFF context with relevant values and functions to be used across the application
        <BffContext.Provider value={{ user, sessionError, fetchBff, checkSession, login, logout }}>
            {children}
        </BffContext.Provider>
    );
};

// Custom hook to use the BFF context easily in other components
export const useBff = (): BffContextProps => useContext(BffContext);