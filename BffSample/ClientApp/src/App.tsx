import React from 'react';
import './App.css';
import { BffProvider, useBff } from './components/Bff';
import { UserClaims } from './components/UserClaims';
import { WeatherForecast } from "./components/WeatherForecast";

export const LogoutButton: React.FC = () => {
    const { logout } = useBff();
    return (
        <button className="logout-button" onClick={logout}>
            Logout
        </button>
    );
};

const App: React.FC = () => (
    <BffProvider baseUrl="https://localhost:5003/bff">
        <div className="app">
            <header className="app-header">
                <div>
                    <h1>Abblix OIDC Server</h1>
                    <span className="subtitle">React SPA secured by a Backend-for-Frontend</span>
                </div>
                <LogoutButton />
            </header>
            <section className="card">
                <UserClaims/>
            </section>
            <section className="card">
                <WeatherForecast/>
            </section>
        </div>
    </BffProvider>
);

export default App
