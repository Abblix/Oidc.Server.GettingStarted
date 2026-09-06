import React from 'react';
import { useBff } from './Bff';

export const UserClaims: React.FC = () => {
    const { user, sessionError } = useBff();

    if (sessionError)
        return <p className="status">No session: {sessionError}</p>;

    if (!user)
        return <p className="status">Checking your session...</p>;

    return (
        <>
            <h2>Your session</h2>
            <p className="hint">Claims the SPA reads through the BFF, never from a token in the browser.</p>
            <dl className="claims">
                {Object.entries(user).map(([claim, value]) => (
                    <React.Fragment key={claim}>
                        <dt>{claim}</dt>
                        <dd>{String(value)}</dd>
                    </React.Fragment>
                ))}
            </dl>
        </>
    );
};
