import { Category, EntryInput, TransactionType } from '../budgetly-api';

const columns = ['data', 'typ', 'kwota', 'kategoria', 'opis'];
export const maxCsvRows = 100;
export const maxCsvBytes = 128 * 1024;

export interface CsvRow {
  number: number;
  entry: EntryInput | null;
  sourceCategory: string;
  selected: boolean;
  status: 'pending' | 'invalid' | 'saved' | 'duplicate' | 'rejected';
  message: string;
}

export function csvTemplate(): string {
  const date = new Intl.DateTimeFormat('sv-SE').format(new Date());
  return '\uFEFF' + [
    columns.join(';'),
    date + ';wydatek;27,50;Zakupy spożywcze;Zakupy',
    date + ';przychód;1000;Wynagrodzenie;Przykładowy przychód'
  ].join('\r\n') + '\r\n';
}

export async function parseCsv(source: string, categories: Category[]): Promise<CsvRow[]> {
  const { default: papa } = await import('papaparse');
  const parsed = papa.parse<string[]>(source, { delimiter: ';', skipEmptyLines: 'greedy' });
  if (parsed.errors.length > 0)
    throw new Error('CSV zawiera niepoprawne cudzysłowy. Popraw plik i wczytaj go ponownie.');

  const [header, ...records] = parsed.data;
  if (header?.length !== columns.length || !columns.every((name, index) => normalize(header[index]) === name))
    throw new Error('Nieprawidłowy nagłówek. Użyj formatu data;typ;kwota;kategoria;opis.');
  if (records.length === 0)
    throw new Error('W pliku nie ma transakcji.');
  if (records.length > maxCsvRows)
    throw new Error('Jeden plik może zawierać maksymalnie 100 transakcji.');

  return records.map((record, index) => {
    const [date, typeText, amountText, sourceCategory, description] = record.map(value => value.trim());
    const number = index + 1;
    if (record.length !== columns.length) return invalid(number, 'Wiersz musi mieć pięć pól.');

    const type = parseType(typeText);
    if (type === null) return invalid(number, 'Typ musi mieć wartość „przychód” albo „wydatek”.');
    if (!validDate(date)) return invalid(number, 'Podaj poprawną datę w formacie RRRR-MM-DD.');
    if (!/^\d{1,9}([.,]\d{1,2})?$/.test(amountText) || Number(amountText.replace(',', '.')) <= 0)
      return invalid(number, 'Podaj dodatnią kwotę z maksymalnie dwoma miejscami po przecinku.');
    if (description.length > 250) return invalid(number, 'Opis może mieć maksymalnie 250 znaków.');

    const matches = categories.filter(item => item.type === type && normalize(item.name) === normalize(sourceCategory));
    return {
      number,
      entry: { date, type, amount: Number(amountText.replace(',', '.')), categoryId: matches.length === 1 ? matches[0].id : '', description },
      sourceCategory,
      selected: true,
      status: 'pending',
      message: ''
    };
  });
}

function normalize(value: string): string {
  return value.trim().toLocaleLowerCase('pl-PL');
}

function parseType(text: string): TransactionType | null {
  switch (normalize(text)) {
    case 'przychód':
    case 'przychod':
      return 1;
    case 'wydatek':
      return 2;
    default:
      return null;
  }
}

function validDate(date: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date < '1900-01-01') return false;
  const parsed = new Date(date + 'T00:00:00Z');
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === date;
}

function invalid(number: number, message: string): CsvRow {
  return { number, entry: null, sourceCategory: '', selected: false, status: 'invalid', message };
}
