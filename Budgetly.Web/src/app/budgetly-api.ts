export type TransactionType = 1 | 2;
export const savingsCategoryId = '55555555-5555-4555-8555-555555555555';
export interface Session { accessToken: string; expiresAtUtc: string; email: string; role: string; }
export interface Category { id: string; name: string; type: TransactionType; canEdit: boolean; }
export interface EntryInput { amount: number; type: TransactionType; date: string; categoryId: string; description: string; }
export interface BudgetWarning { limit: number; projectedSpending: number; }
export interface Entry extends EntryInput { id: string; budgetWarning: BudgetWarning | null; goalId: string | null; isSavings: boolean; }
export interface EntryPage { items: Entry[]; total: number; page: number; pageSize: number; }
export interface MonthSpending { month: string; spending: number; }
export interface Summary {
  income: number; spending: number; savings: number; balance: number; limit: number | null; highPriority: boolean;
  categories: { categoryId: string; name: string; spending: number }[];
  spendingTrend: MonthSpending[];
}
export interface GoalInput { name: string; target: number; }
export interface Goal extends GoalInput { id: string; saved: number; completedAtUtc: string | null; }
export interface GoalContribution { amount: number; date: string; }
export interface GoalContributionResult { goal: Goal; transaction: Entry; }

export class ApiError extends Error {
  constructor(public readonly status: number, message: string, public readonly code?: string) { super(message); }
}

export async function request<T>(path: string, session: Session | null, method = 'GET', body?: object): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body) headers['Content-Type'] = 'application/json';
  if (session) headers['Authorization'] = 'Bearer ' + session.accessToken;
  const response = await fetch('/api' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const errors = Object.values(problem.errors ?? {}).flat().filter(x => typeof x === 'string');
    const retryAfter = Number(response.headers.get('Retry-After'));
    const rateLimitMessage = retryAfter > 0
      ? 'Za dużo żądań. Spróbuj ponownie za ' + retryAfter + ' s.'
      : 'Za dużo żądań. Spróbuj ponownie za chwilę.';
    const message = errors.join(' ') || (response.status === 401 ? 'Sprawdź dane logowania lub zaloguj się ponownie.'
      : response.status === 429 ? rateLimitMessage
      : problem.title || 'Nie udało się wykonać tej operacji.');
    throw new ApiError(response.status, message, typeof problem.code === 'string' ? problem.code : undefined);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
