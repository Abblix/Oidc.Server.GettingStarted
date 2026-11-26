import { useState } from 'react';
import { endMySessions, ValidationError, type FieldErrors } from '../api/http';

// DEMO ONLY: one button that changes the signed-in user's security stamp, so the sample's session end can be
// watched without editing the database. A real account page reaches the same call through a password change.
export default function EndSessions() {
  const [errors, setErrors] = useState<FieldErrors>({});
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    setErrors({});
    setDone(false);
    try {
      await endMySessions();
      setDone(true);
    } catch (error) {
      setErrors(error instanceof ValidationError ? error.errors : { '': ['Something went wrong. Please try again.'] });
    }
    setBusy(false);
  }

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h1 className="text-xl font-semibold text-slate-900 dark:text-white">End my sessions (demo)</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Changes your security stamp, as a password change would. Every session your account holds at this
          provider ends the next time it is used, this one included.
        </p>
      </header>

      {errors['']?.map((message) => (
        <p key={message} className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:bg-rose-950/50 dark:text-rose-300">
          {message}
        </p>
      ))}

      {done && (
        <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300">
          Your security stamp has changed.
        </p>
      )}

      <button
        type="button"
        onClick={submit}
        disabled={busy}
        className="w-full rounded-lg bg-sky-600 px-4 py-2.5 font-medium text-white transition hover:bg-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/50 disabled:opacity-60"
      >
        {busy ? 'Ending...' : 'End my sessions'}
      </button>
    </div>
  );
}
