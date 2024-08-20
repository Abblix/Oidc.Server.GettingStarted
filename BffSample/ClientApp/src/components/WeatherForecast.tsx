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
    }, []);


    const contents = loading
        ? <p><em>Loading...</em></p>
        : (
            <table className="table table-striped" aria-labelledby="tableLabel">
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
                        <td align="center">{forecast.temperatureC}</td>
                        <td align="center">{forecast.temperatureF}</td>
                        <td>{forecast.summary}</td>
                    </tr>
                ))}
                </tbody>
            </table>
        );

    return (
        <div>
            <h2 id="tableLabel">Weather forecast</h2>
            <p>This component demonstrates fetching data from the server.</p>
            {contents}
        </div>
    );
};