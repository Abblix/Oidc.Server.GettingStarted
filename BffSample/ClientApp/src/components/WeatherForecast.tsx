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
}

export const WeatherForecast: React.FC = () => {
    const { fetchBff } = useBff();
    const [state, setState] = useState<State>({ forecasts: [], loading: true });
    const { forecasts, loading } = state;

    useEffect(() => {
        fetchBff('weatherforecast')
            .then(response => response.json())
            .then(data => setState({forecasts: data, loading: false}));
    }, [fetchBff]);


    const contents = loading
        ? <p className="status">Loading...</p>
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