import React from 'react';
import { useBff } from './bff';

export const UserClaims: React.FC = () => {
    const { user } = useBff();

    if (!user)
        return <div>Checking user session...</div>;

    return (
        <>
            <h2>User Claims</h2>
            <p>This component displays claims received from the OpenID Connect server.</p>
            {Object.entries(user).map(([claim, value]) => (
                <div key={claim}>
                    <strong>{claim}</strong>: {String(value)}
                </div>
            ))}
        </>
    );
};
