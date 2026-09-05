import React, { useEffect, useState } from 'react';
import { useBff } from "./Bff";

interface Forecast {
    date: string;
    temperatureC: number;
    temperatureF: number;
    summary: string;
}

interface State {
    forecasts: Forecast[];
    loading: boolean;
    error: string | null;
}

export const WeatherForecast: React.FC = () => {
    const { fetchBff, login } = useBff();
    const [state, setState] = useState<State>({ forecasts: [], loading: true, error: null });
    const { forecasts, loading, error } = state;

    useEffect(() => {
        const fail = (message: string, cause?: unknown) => {
            // Keep the original in the console: the message on screen is for the reader, the cause
            // is for whoever debugs it.
            if (cause) console.error('Weather forecast call failed:', cause);
            setState({ forecasts: [], loading: false, error: message });
        };

        // Remembers that a redirect already sent us to sign in once, so a second one is
        // reported rather than followed.
        const retriedKey = 'bff.weatherforecast.retried';

        // redirect: 'manual' is what makes an expired session visible. The forwarder sits behind
        // RequireAuthorization with OpenIdConnect as the challenge scheme, so a request without a
        // session is answered with a 302 to the provider rather than a 401 - and a browser fetch
        // that follows it lands on a cross-origin page with no CORS headers, failing as a network
        // error that says nothing. Caught here, the redirect means what it is: sign in again.
        fetchBff('weatherforecast', { redirect: 'manual' })
            .then(async response => {
                if (response.type === 'opaqueredirect') {
                    // One attempt only. A redirect answered to a session that IS valid would
                    // otherwise bounce the reader through the provider forever with nothing on
                    // screen - the same silent failure this panel exists to end.
                    if (sessionStorage.getItem(retriedKey)) {
                        fail('the BFF keeps asking for a new sign-in - check its session cookie');
                        return;
                    }

                    sessionStorage.setItem(retriedKey, '1');
                    login();
                    return;
                }

                sessionStorage.removeItem(retriedKey);

                if (!response.ok) {
                    // Both codes come from the API rather than from the BFF: 401 when it will not
                    // accept the token at all (expired, wrong audience, wrong issuer), 403 when it
                    // accepts the token but the weather scope is missing.
                    fail(response.status === 401 || response.status === 403
                        ? `the API did not accept the access token (${response.status})`
                        : `the API answered ${response.status}`);
                    return;
                }

                try {
                    setState({ forecasts: await response.json(), loading: false, error: null });
                } catch (cause) {
                    fail('the API answered something that is not JSON', cause);
                }
            })
            .catch(cause => fail('the BFF could not be reached - is it running?', cause));
    }, [fetchBff, login]);

    const contents = loading
        ? <p className="status">Loading...</p>
        : error
            ? <p className="status">No forecast: {error}</p>
            : (
                <table className="table" aria-labelledby="tableLabel">
                    <thead>
                    <tr>
                        <th>Date</th>
                        <th>Temp. (C)</th>
                        <th>Temp. (F)</th>
                        <th>Summary</th>
                    </tr>
                    </thead>
                    <tbody>
                    {forecasts.map((forecast, index) => (
                        <tr key={index}>
                            <td>{forecast.date}</td>
                            <td>{forecast.temperatureC}</td>
                            <td>{forecast.temperatureF}</td>
                            <td>{forecast.summary}</td>
                        </tr>
                    ))}
                    </tbody>
                </table>
            );

    return (
        <div>
            <h2 id="tableLabel">Weather forecast</h2>
            <p className="hint">Fetched from a protected API; the BFF attaches the access token server-side.</p>
            {contents}
        </div>
    );
};
