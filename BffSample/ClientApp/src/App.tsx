import React from 'react';
import './App.css';
import { BffProvider, useBff } from './components/bff';
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
        <div className="card">
            <UserClaims/>
        </div>
        <div className="card">
            <WeatherForecast/>
        </div>
        <div className="card">
            <LogoutButton />
        </div>
    </BffProvider>
);

export default App
