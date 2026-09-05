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
    const { fetchBff } = useBff();
    const [state, setState] = useState<State>({ forecasts: [], loading: true, error: null });
    const { forecasts, loading, error } = state;

    useEffect(() => {
        // A refused call answers with a status and an empty body, so parsing before checking turns
        // every refusal into a JSON error and leaves the panel loading forever. The status is the
        // interesting part: 401 means the session went away, 403 that the token lacks the scope the
        // API wants, and anything else is worth showing rather than swallowing.
        fetchBff('weatherforecast')
            .then(async response => {
                if (!response.ok) {
                    const reason = response.status === 401
                        ? 'the session is gone - sign in again'
                        : response.status === 403
                            ? 'the access token is not accepted by the API'
                            : `the API answered ${response.status}`;

                    setState({ forecasts: [], loading: false, error: reason });
                    return;
                }

                setState({ forecasts: await response.json(), loading: false, error: null });
            })
            .catch(() => setState({
                forecasts: [],
                loading: false,
                error: 'the BFF could not be reached - is it running?',
            }));
    }, [fetchBff]);

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
