import { Component, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiError, Category, Entry, EntryInput, EntryPage, Goal, GoalInput, Session, Summary, TransactionType, request } from './budgetly-api';

function today(): string {
  const now = new Date();
  now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
  return now.toISOString().slice(0, 10);
}

function storedSession(): Session | null {
  try {
    const session = JSON.parse(sessionStorage.getItem('budgetly.session') ?? 'null') as Session | null;
    return session && Date.parse(session.expiresAtUtc) > Date.now() ? session : null;
  } catch {
    return null;
  }
}

@Component({
  selector: 'app-root',
  imports: [CurrencyPipe, DatePipe, FormsModule],
  templateUrl: './app.component.html'
})
export class AppComponent {
  readonly session = signal<Session | null>(storedSession());
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly categories = signal<Category[]>([]);
  readonly history = signal<EntryPage | null>(null);
  readonly summary = signal<Summary | null>(null);
  readonly goals = signal<Goal[]>([]);
  readonly adminCount = signal<number | null>(null);
  authMode: 'login' | 'register' = 'login';
  email = '';
  password = '';
  month = today().slice(0, 7);
  limit: number | null = null;
  highPriority = false;
  entry: EntryInput = this.emptyEntry();
  editId: string | null = null;
  filterType = '';
  filterCategory = '';
  page = 1;
  categoryName = '';
  categoryType: TransactionType = 2;
  categoryEditId: string | null = null;
  goal: GoalInput = { name: '', target: 0, saved: 0 };
  goalId: string | null = null;

  constructor() {
    if (this.session()) void this.run(() => this.refresh(true));
  }

  async authenticate() {
    await this.run(async () => {
      const credentials = { email: this.email, password: this.password };
      if (this.authMode === 'register') await request('/auth/register', null, 'POST', credentials);
      const session = await request<Session>('/auth/login', null, 'POST', credentials);
      this.session.set(session);
      sessionStorage.setItem('budgetly.session', JSON.stringify(session));
      this.password = '';
      await this.refresh(true);
    });
  }

  logout() {
    sessionStorage.removeItem('budgetly.session');
    this.session.set(null);
    this.summary.set(null);
    this.history.set(null);
    this.categories.set([]);
    this.goals.set([]);
    this.adminCount.set(null);
    this.message.set('');
    this.error.set('');
    this.page = 1;
    this.filterType = '';
    this.filterCategory = '';
    this.limit = null;
    this.highPriority = false;
    this.clearEntry();
    this.clearGoal();
    this.categoryName = '';
    this.categoryEditId = null;
  }

  async selectMonth() {
    if (!this.month) return;
    this.page = 1;
    await this.run(() => this.refresh(true));
  }

  async changePage(page: number) {
    this.page = page;
    await this.run(() => this.loadHistory());
  }

  async saveEntry() {
    await this.run(async () => {
      const result = await this.call<Entry>('/transactions' + (this.editId ? '/' + this.editId : ''), this.editId ? 'PUT' : 'POST', this.entry);
      this.clearEntry();
      if (result.date.slice(0, 7) !== this.month) {
        this.month = result.date.slice(0, 7);
        this.page = 1;
        await this.refresh(true);
      } else {
        this.page = 1;
        await this.refresh();
      }
      const warning = result.budgetWarning;
      this.message.set(warning
        ? 'Zapisano. Wydatki przekraczają limit (' + warning.projectedSpending.toFixed(2) + ' z ' + warning.limit.toFixed(2) + ' zł).'
        : 'Zapisano transakcję.');
    });
  }

  editEntry(item: Entry) {
    this.editId = item.id;
    this.entry = { amount: Math.abs(item.amount), date: item.date, type: item.type,
      categoryId: item.categoryId, description: item.description ?? '' };
  }

  clearEntry() {
    this.editId = null;
    this.entry = this.emptyEntry();
  }

  async deleteEntry(id: string) {
    if (!window.confirm('Usunąć transakcję?')) return;
    await this.run(async () => {
      await this.call('/transactions/' + id, 'DELETE');
      this.page = 1;
      if (this.editId === id) this.clearEntry();
      await this.refresh();
    });
  }

  async saveBudget() {
    await this.run(async () => {
      await this.call('/budgets/' + this.month, 'PUT', { limit: this.limit, highPriority: this.highPriority });
      await this.refresh(true);
      this.message.set('Zapisano limit miesięczny.');
    });
  }

  async deleteBudget() {
    if (!window.confirm('Usunąć limit na ten miesiąc?')) return;
    await this.run(async () => {
      await this.call('/budgets/' + this.month, 'DELETE');
      await this.refresh(true);
    });
  }

  editCategory(category: Category) {
    this.categoryEditId = category.id;
    this.categoryName = category.name;
    this.categoryType = category.type;
  }

  async saveCategory() {
    await this.run(async () => {
      const path = '/categories' + (this.categoryEditId ? '/' + this.categoryEditId : '');
      await this.call(path, this.categoryEditId ? 'PUT' : 'POST', { name: this.categoryName, type: this.categoryType });
      this.categoryEditId = null;
      this.categoryName = '';
      await this.refresh();
    });
  }

  async deleteCategory(id: string) {
    if (!window.confirm('Usunąć kategorię?')) return;
    await this.run(async () => {
      await this.call('/categories/' + id, 'DELETE');
      if (this.categoryEditId === id) {
        this.categoryEditId = null;
        this.categoryName = '';
      }
      if (this.filterCategory === id) this.filterCategory = '';
      if (this.entry.categoryId === id) this.entry.categoryId = '';
      await this.refresh();
    });
  }

  async saveGoal() {
    await this.run(async () => {
      await this.call('/goals' + (this.goalId ? '/' + this.goalId : ''), this.goalId ? 'PUT' : 'POST', this.goal);
      this.clearGoal();
      this.goals.set(await this.call<Goal[]>('/goals'));
    });
  }

  editGoal(goal: Goal) {
    this.goalId = goal.id;
    this.goal = { name: goal.name, target: goal.target, saved: goal.saved };
  }

  clearGoal() {
    this.goalId = null;
    this.goal = { name: '', target: 0, saved: 0 };
  }

  async deleteGoal(id: string) {
    if (!window.confirm('Usunąć cel?')) return;
    await this.run(async () => {
      await this.call('/goals/' + id, 'DELETE');
      if (this.goalId === id) this.clearGoal();
      this.goals.set(await this.call<Goal[]>('/goals'));
    });
  }

  categoryLabel(id: string) {
    return this.categories().find(category => category.id === id)?.name ?? 'Kategoria';
  }

  get entryCategories() {
    return this.categories().filter(category => category.type === this.entry.type);
  }

  private async refresh(resetBudget = false) {
    const [categories, summary, goals] = await Promise.all([
      this.call<Category[]>('/categories'), this.call<Summary>('/budgets/' + this.month), this.call<Goal[]>('/goals')
    ]);
    this.categories.set(categories);
    this.summary.set(summary);
    this.goals.set(goals);
    if (resetBudget) {
      this.limit = summary.limit;
      this.highPriority = summary.highPriority;
    }
    await this.loadHistory();
    if (this.session()?.role === 'ADMIN' && this.adminCount() === null)
      this.adminCount.set((await this.call<{ userCount: number }>('/admin/summary')).userCount);
  }

  private async loadHistory() {
    const [year, month] = this.month.split('-').map(Number);
    const days = new Date(year, month, 0).getDate();
    const params = new URLSearchParams({ from: this.month + '-01', to: this.month + '-' + days, page: String(this.page) });
    if (this.filterType) params.set('type', this.filterType);
    if (this.filterCategory) params.set('categoryId', this.filterCategory);
    this.history.set(await this.call<EntryPage>('/transactions?' + params));
  }

  private emptyEntry(): EntryInput {
    return { amount: 0, type: 2, date: today(), categoryId: '', description: '' };
  }

  private call<T>(path: string, method?: string, body?: object) {
    return request<T>(path, this.session(), method, body);
  }

  private async run(action: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await action();
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && this.session()) this.logout();
      this.error.set(error instanceof ApiError ? error.message : 'Nie udało się połączyć z Budgetly. Spróbuj ponownie.');
    } finally {
      this.busy.set(false);
    }
  }
}
