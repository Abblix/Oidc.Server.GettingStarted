import type { components } from './schema';

// The request/response shapes are generated from the server's OpenAPI document (npm run gen:api), so
// they cannot drift from the C# contract. If the server changes a field, the client stops compiling.
export type LoginRequest = components['schemas']['LoginRequest'];
export type RegisterRequest = components['schemas']['RegisterRequest'];
type AuthSuccessResponse = components['schemas']['AuthSuccessResponse'];

export type FieldErrors = Record<string, string[]>;

// Thrown for a 400 ValidationProblemDetails. Keys are normalised to lowercase field names (and the
// empty string for form-level errors), so components read errors.email, errors.password, and so on
// regardless of the casing the server used in its ModelState keys.
export class ValidationError extends Error {
  constructor(public readonly errors: FieldErrors) {
    super('Validation failed');
    this.name = 'ValidationError';
  }
}

function antiforgeryHeader(): Record<string, string> {
  const cookie = document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='));
  if (cookie === undefined) return {};
  return { 'X-CSRF-TOKEN': decodeURIComponent(cookie.slice('XSRF-TOKEN='.length)) };
}

function normalise(errors: Record<string, string[]> | undefined): FieldErrors {
  const result: FieldErrors = {};
  for (const [key, messages] of Object.entries(errors ?? {})) {
    const field = key.replace(/^\$\./, '').toLowerCase();
    result[field] = (result[field] ?? []).concat(messages);
  }
  return result;
}

async function post<TBody>(url: string, body: TBody): Promise<AuthSuccessResponse> {
  const response = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...antiforgeryHeader(),
    },
    body: JSON.stringify(body),
  });

  if (response.ok) return (await response.json()) as AuthSuccessResponse;

  if (response.status === 400) {
    const problem = (await response.json()) as { errors?: Record<string, string[]> };
    throw new ValidationError(normalise(problem.errors));
  }

  throw new Error(`Request failed with status ${response.status}`);
}

export const signIn = (body: LoginRequest): Promise<AuthSuccessResponse> => post('/api/auth/login', body);
export const signUp = (body: RegisterRequest): Promise<AuthSuccessResponse> => post('/api/auth/register', body);
