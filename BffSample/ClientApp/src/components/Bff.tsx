import { useCallback, useEffect, useState, ReactNode, FC } from 'react';
import { BffContext, type UserClaims } from './useBff';

// The static header every request to the BFF carries; BffSample/Program.cs checks for the same name and value
const CsrfHeaderName = 'X-CSRF';
const CsrfHeaderValue = '1';

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
                // The BFF refuses a request without this header (RFC 10017 section 6.1.3.3.2). A page on another
                // origin, a sibling on the same site included, cannot add a custom header to a request without the
                // BFF approving it in a CORS preflight, and the BFF approves none.
                const headers = new Headers(options.headers);
                headers.set(CsrfHeaderName, CsrfHeaderValue);
                return await fetch(`${normalizedBaseUrl}/${endpoint}`, { ...options, headers });
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

    // Full RP-initiated logout. The POST carries the CSRF header, so no other page can end the session; the BFF
    // clears its cookie and answers with an address of its own, which sends the browser on to the provider's
    // end-session endpoint and back. A fetch cannot follow that redirect itself, and the provider's address never
    // reaches this script, because it carries the ID token.
    const logout = useCallback(async (): Promise<void> => {
        try {
            const response = await fetchBff('logout', { method: 'POST' });
            if (!response.ok) {
                setSessionError(`the BFF answered ${response.status} to the logout`);
                return;
            }

            const { next } = await response.json() as { next: string };
            window.location.assign(next);
        } catch (cause) {
            console.error('Logout failed:', cause);
            setSessionError('the logout did not reach the BFF');
        }
    }, [fetchBff]);

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