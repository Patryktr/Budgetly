import { CurrencyPipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { ApiError, Category, Entry, Session, request } from '../budgetly-api';
import { CsvRow, csvTemplate, maxCsvBytes, parseCsv } from './csv-format';

@Component({
  selector: 'app-csv-import',
  imports: [CurrencyPipe],
  template: `
    <details class="panel">
      <summary>Importuj transakcje z CSV</summary>
      <p>Użyj szablonu Budgetly. Wybierz plik, sprawdź podgląd i zaznacz transakcje do zapisania.</p>
      <div class="actions">
        <button class="secondary" type="button" (click)="downloadTemplate()">Pobierz szablon CSV</button>
        <label>Plik CSV<input type="file" accept=".csv,text/csv" [disabled]="disabled() || loading()" (change)="readFile($event)"></label>
      </div>
      @if (error()) { <p class="notice error" role="alert">{{ error() }}</p> }
      @if (rows().length > 0) {
        <form (submit)="save($event)">
          <fieldset [disabled]="disabled() || loading()">
            <div class="table-wrap">
              <table aria-label="Podgląd importu CSV">
                <thead><tr><th scope="col">Importuj</th><th scope="col">Pozycja</th><th scope="col">Data i typ</th>
                  <th scope="col">Kwota</th><th scope="col">Kategoria</th><th scope="col">Opis</th><th scope="col">Wynik</th></tr></thead>
                <tbody>
                  @for (row of rows(); track row.number) {
                    <tr>
                      <td><input type="checkbox" [checked]="row.selected" [disabled]="row.status !== 'pending'"
                        [attr.aria-label]="'Importuj pozycję ' + row.number" (change)="select(row, $event)"></td>
                      <td>{{ row.number }}</td>
                      @if (row.entry; as entry) {
                        <td>{{ entry.date }}<br>{{ entry.type === 1 ? 'Przychód' : 'Wydatek' }}</td>
                        <td>{{ entry.amount | currency:'PLN' }}</td>
                        <td>
                          <select [attr.aria-label]="'Kategoria pozycji ' + row.number" [disabled]="row.status !== 'pending'"
                            (change)="changeCategory(row, $event)">
                            <option value="" [selected]="!entry.categoryId">{{ row.sourceCategory ? 'Wybierz (' + row.sourceCategory + ')' : 'Wybierz kategorię' }}</option>
                            @for (category of categories(); track category.id) {
                              @if (category.type === entry.type) {
                                <option [value]="category.id" [selected]="category.id === entry.categoryId">{{ category.name }}</option>
                              }
                            }
                          </select>
                        </td>
                        <td>{{ entry.description || '—' }}</td>
                      } @else {
                        <td colspan="4">Popraw pozycję w pliku, aby ją wczytać.</td>
                      }
                      <td>{{ row.message || (row.entry?.categoryId ? 'Gotowa' : 'Wybierz kategorię') }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="actions">
              <button type="submit" [disabled]="!selected().length || missingCategory()">Zapisz zaznaczone ({{ selected().length }})</button>
              @if (missingCategory()) { <span>Uzupełnij kategorie zaznaczonych pozycji albo je odznacz.</span> }
            </div>
          </fieldset>
        </form>
        @if (result(); as summary) { <p role="status">{{ summary }}</p> }
      }
    </details>
  `,
  styles: [`
    :host { display: block; }
    summary { cursor: pointer; font-size: 1.2rem; font-weight: 700; }
    fieldset { padding: 0; margin: 16px 0 0; border: 0; min-width: 0; }
    td:nth-child(5) { min-width: 160px; }
    td:nth-child(n+6) { overflow-wrap: anywhere; }
    .table-wrap + .actions { margin-top: 12px; }
  `]
})
export class CsvImportComponent {
  readonly session = input.required<Session>();
  readonly categories = input.required<Category[]>();
  readonly disabled = input(false);
  readonly importRunning = output<boolean>();
  readonly imported = output<void>();
  readonly rows = signal<CsvRow[]>([]);
  readonly error = signal('');
  readonly loading = signal(false);
  readonly selected = computed(() => this.rows().filter(row => row.selected && row.status === 'pending'));
  readonly missingCategory = computed(() => this.selected().some(row => !row.entry?.categoryId));
  readonly result = computed(() => {
    const rows = this.rows();
    const saved = rows.filter(row => row.status === 'saved').length;
    const duplicates = rows.filter(row => row.status === 'duplicate').length;
    const rejected = rows.filter(row => row.status === 'rejected').length;
    return saved + duplicates + rejected > 0
      ? 'Zapisane: ' + saved + ' · Duplikaty: ' + duplicates + ' · Odrzucone: ' + rejected : '';
  });

  async readFile(event: Event) {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0];
    if (!file || this.loading() || this.disabled()) return;
    target.value = '';
    this.rows.set([]);
    this.error.set('');
    this.loading.set(true);
    try {
      if (!file.name.toLowerCase().endsWith('.csv')) throw new Error('Wybierz plik z rozszerzeniem .csv.');
      if (file.size > maxCsvBytes) throw new Error('Plik CSV może mieć maksymalnie 128 KB.');
      const data = await file.arrayBuffer();
      let source: string;
      try {
        source = new TextDecoder('utf-8', { fatal: true }).decode(data);
      } catch {
        throw new Error('Zapisz plik CSV w kodowaniu UTF-8.');
      }
      this.rows.set(await parseCsv(source, this.categories()));
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Nie udało się odczytać pliku.');
    } finally {
      this.loading.set(false);
    }
  }

  select(row: CsvRow, event: Event) {
    this.update(row, { selected: (event.target as HTMLInputElement).checked });
  }

  changeCategory(row: CsvRow, event: Event) {
    if (row.entry) this.update(row, { entry: { ...row.entry, categoryId: (event.target as HTMLSelectElement).value } });
  }

  async save(event: Event) {
    event.preventDefault();
    if (!this.selected().length || this.missingCategory() || this.loading() || this.disabled()) return;
    this.loading.set(true);
    this.importRunning.emit(true);
    this.error.set('');
    let changed = false;
    try {
      for (const row of this.selected()) {
        try {
          const result = await request<Entry>('/transactions', this.session(), 'POST', { ...row.entry, fromCsv: true });
          changed = true;
          this.update(row, { status: 'saved', selected: false, message: result.budgetWarning
            ? 'Zapisano. Wydatek przekracza zwykły limit.' : 'Zapisano.' });
        } catch (error) {
          if (!(error instanceof ApiError) || error.status >= 500 || error.status === 401 || error.status === 429) {
            this.error.set(error instanceof ApiError ? error.message : 'Połączenie przerwane. Możesz wznowić import.');
            break;
          }
          this.update(row, { status: error.code === 'duplicate_csv' ? 'duplicate' : 'rejected',
            selected: false, message: error.message });
        }
      }
    } finally {
      this.loading.set(false);
      this.importRunning.emit(false);
      if (changed) this.imported.emit();
    }
  }

  downloadTemplate() {
    const url = URL.createObjectURL(new Blob([csvTemplate()], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'budgetly-szablon.csv';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  private update(row: CsvRow, changes: Partial<CsvRow>) {
    this.rows.update(rows => rows.map(item => item.number === row.number ? { ...item, ...changes } : item));
  }
}
