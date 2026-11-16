export type TransactionType = 1 | 2;
export interface Session { accessToken: string; expiresAtUtc: string; email: string; role: string; }
export interface Category { id: string; name: string; type: TransactionType; canEdit: boolean; }
export interface EntryInput { amount: number; type: TransactionType; date: string; categoryId: string; description: string; }
export interface BudgetWarning { limit: number; projectedSpending: number; }
export interface Entry extends EntryInput { id: string; budgetWarning: BudgetWarning | null; }
export interface EntryPage { items: Entry[]; total: number; page: number; pageSize: number; }
export interface Summary {
  income: number; spending: number; balance: number; limit: number | null; highPriority: boolean;
  categories: { categoryId: string; name: string; spending: number }[];
}
export interface GoalInput { name: string; target: number; saved: number; }
export interface Goal extends GoalInput { id: string; completedAtUtc: string | null; }

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) { super(message); }
}

export async function request<T>(path: string, session: Session | null, method = 'GET', body?: object): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body) headers['Content-Type'] = 'application/json';
  if (session) headers['Authorization'] = 'Bearer ' + session.accessToken;
  const response = await fetch('/api' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const errors = Object.values(problem.errors ?? {}).flat().filter(x => typeof x === 'string');
    const message = errors.join(' ') || (response.status === 401 ? 'Sprawdź dane logowania lub zaloguj się ponownie.'
      : response.status === 429 ? 'Za dużo prób. Spróbuj ponownie za minutę.'
      : problem.title || 'Nie udało się wykonać tej operacji.');
    throw new ApiError(response.status, message);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
