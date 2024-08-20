import React, { createContext, useContext, useEffect, useState, ReactNode, FC } from 'react';

// Define the shape of the BFF context
interface BffContextProps {
    user: any;
    fetchBff: (endpoint: string, options?: RequestInit) => Promise<Response>;
    checkSession: () => Promise<void>;
    login: () => void;
    logout: () => Promise<void>;
}

// Creating a context for BFF to share state and functions across the application
const BffContext = createContext<BffContextProps>({
    user: null,
    fetchBff: async () => new Response(),
    checkSession: async () => {},
    login: () => {},
    logout: async () => {}
});

interface BffProviderProps {
    baseUrl: string;
    children: ReactNode;
}

export const BffProvider: FC<BffProviderProps> = ({ baseUrl, children }) => {
    const [user, setUser] = useState<any>(null);

    // Normalize the base URL by removing a trailing slash to avoid inconsistent URLs
    if (baseUrl.endsWith('/')) {
        baseUrl = baseUrl.slice(0, -1);
    }

    const fetchBff = async (endpoint: string, options: RequestInit = {}): Promise<Response> => {
        try {
            // The fetch function includes credentials to handle cookies, which are necessary for authentication
            return await fetch(`${baseUrl}/${endpoint}`, {
                credentials: 'include',
                ...options
            });
        } catch (error) {
            console.error(`Error during ${endpoint} call:`, error);
            throw error;
        }
    };

    // The login function redirects to the login page when user needs to authenticate
    const login = (): void => {
        window.location.replace(`${baseUrl}/login`);
    };

    // The checkSession function is responsible for verifying the user session on initial render
    const checkSession = async (): Promise<void> => {
        const response = await fetchBff('check_session');

        if (response.ok) {
            // If the session is valid, update the user state with the received claims data
            setUser(await response.json());
        } else if (response.status === 401) {
            // If the user is not authenticated, redirect him to the login page
            login();
        } else {
            console.error('Unexpected response from checking session:', response);
        }
    };

    // Function to log out the user
    const logout = async (): Promise<void> => {
        const response = await fetchBff('logout', { method: 'POST' });

        if (response.ok) {
            // Redirect to the home page after successful logout
            window.location.replace('/');
        } else {
            console.error('Logout failed:', response);
        }
    };

    // useEffect is used to run the checkSession function once the component mounts
    // This ensures the session is checked immediately when the app loads
    useEffect(() => { checkSession(); }, []);

    return (
        // Providing the BFF context with relevant values and functions to be used across the application
        <BffContext.Provider value={{ user, fetchBff, checkSession, login, logout }}>
            {children}
        </BffContext.Provider>
    );
};

// Custom hook to use the BFF context easily in other components
export const useBff = (): BffContextProps => useContext(BffContext);

// Export HOC to provide access to BFF Context
export const withBff = (Component: React.ComponentType<any>) => (props: any) =>
    <BffContext.Consumer>
        {context => <Component {...props} bffContext={context} />}
    </BffContext.Consumer>;