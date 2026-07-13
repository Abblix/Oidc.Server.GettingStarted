import { useState, type FormEvent } from 'react';
import Field from './Field';
import { signUp, ValidationError, type FieldErrors } from '../api/http';

interface SignUpProps {
  requestUri: string;
  onSwitch: () => void;
}

export default function SignUp({ requestUri, onSwitch }: SignUpProps) {
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setErrors({});
    try {
      const { redirectUrl } = await signUp({ email, name, password, confirmPassword, requestUri });
      // Registration doubles as the first sign-in; resume the OIDC flow already authenticated.
      window.location.assign(redirectUrl);
    } catch (error) {
      setErrors(error instanceof ValidationError ? error.errors : { '': ['Something went wrong. Please try again.'] });
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5" noValidate>
      <header className="space-y-1">
        <h1 className="text-xl font-semibold text-slate-900 dark:text-white">Create account</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">Register to continue.</p>
      </header>

      {errors['']?.map((message) => (
        <p key={message} className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:bg-rose-950/50 dark:text-rose-300">
          {message}
        </p>
      ))}

      <Field id="email" label="Email" type="email" value={email} autoComplete="email" onChange={setEmail} errors={errors.email} />
      <Field id="name" label="Full name" value={name} autoComplete="name" onChange={setName} errors={errors.name} />
      <Field id="password" label="Password" type="password" value={password} autoComplete="new-password" onChange={setPassword} errors={errors.password} />
      <Field
        id="confirmPassword"
        label="Confirm password"
        type="password"
        value={confirmPassword}
        autoComplete="new-password"
        onChange={setConfirmPassword}
        errors={errors.confirmpassword}
      />

      <button
        type="submit"
        disabled={busy}
        className="w-full rounded-lg bg-sky-600 px-4 py-2.5 font-medium text-white transition hover:bg-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/50 disabled:opacity-60"
      >
        {busy ? 'Creating account...' : 'Create account'}
      </button>

      <p className="text-center text-sm text-slate-500 dark:text-slate-400">
        Already have an account?{' '}
        <button type="button" onClick={onSwitch} className="font-medium text-sky-600 hover:text-sky-500 dark:text-sky-400">
          Sign in
        </button>
      </p>
    </form>
  );
}
