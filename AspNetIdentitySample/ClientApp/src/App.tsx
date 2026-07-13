import { useState } from 'react';
import SignIn from './components/SignIn';
import SignUp from './components/SignUp';

type Screen = 'signin' | 'signup';

function screenFromPath(): Screen {
  return window.location.pathname.toLowerCase().includes('/register') ? 'signup' : 'signin';
}

function requestUriFromQuery(): string {
  return new URLSearchParams(window.location.search).get('request_uri') ?? '';
}

export default function App() {
  const [screen, setScreen] = useState<Screen>(screenFromPath);

  // request_uri is stable for the life of the page: the OIDC library put it in the query, and both
  // screens post it back so the authorization flow can resume after sign-in or sign-up.
  const requestUri = requestUriFromQuery();

  function switchTo(next: Screen) {
    // Keep the URL honest without a full reload, preserving the request_uri query.
    const path = next === 'signup' ? '/Auth/Register' : '/Auth/Login';
    window.history.pushState(null, '', `${path}${window.location.search}`);
    setScreen(next);
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-6 bg-slate-100 p-4 dark:bg-slate-950">
      <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-8 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        {screen === 'signin' ? (
          <SignIn requestUri={requestUri} onSwitch={() => switchTo('signup')} />
        ) : (
          <SignUp requestUri={requestUri} onSwitch={() => switchTo('signin')} />
        )}
      </div>
      <p className="text-xs text-slate-400 dark:text-slate-600">
        Powered by{' '}
        <a
          href="https://github.com/Abblix/Oidc.Server"
          target="_blank"
          rel="noreferrer"
          className="font-medium text-slate-500 hover:text-sky-600 dark:text-slate-500 dark:hover:text-sky-400"
        >
          Abblix OIDC Server
        </a>
      </p>
    </div>
  );
}
