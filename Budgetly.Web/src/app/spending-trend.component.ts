import { CurrencyPipe } from '@angular/common';
import { afterRenderEffect, Component, DestroyRef, ElementRef, inject, input, viewChild } from '@angular/core';
import { BarController, BarElement, CategoryScale, Chart, LinearScale, Tooltip } from 'chart.js';
import { MonthSpending } from './budgetly-api';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip);

const amountFormat = new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' });
const monthFormat = new Intl.DateTimeFormat('pl-PL', { month: 'short', year: 'numeric', timeZone: 'UTC' });

@Component({
  selector: 'app-spending-trend',
  imports: [CurrencyPipe],
  template: `
    <section class="panel" aria-labelledby="spending-trend-title">
      <h2 id="spending-trend-title">Wydatki w ostatnich 6 miesiącach</h2>
      <div class="chart">
        <canvas #canvas aria-hidden="true"></canvas>
      </div>
      <details>
        <summary>Dane wykresu</summary>
        <table aria-label="Wydatki w poszczególnych miesiącach">
          <thead><tr><th scope="col">Miesiąc</th><th scope="col">Wydatki</th></tr></thead>
          <tbody>
            @for (item of series(); track item.month) {
              <tr><td>{{ label(item.month) }}</td><td>{{ item.spending | currency:'PLN' }}</td></tr>
            }
          </tbody>
        </table>
      </details>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .chart { position: relative; height: clamp(210px, 32vw, 300px); }
    details { margin-top: 12px; color: #48685f; }
    summary { cursor: pointer; }
    table { margin-top: 8px; }
  `]
})
export class SpendingTrendComponent {
  readonly series = input.required<MonthSpending[]>();
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart<'bar', number[], string>;

  constructor() {
    afterRenderEffect(() => {
      const values = this.series();
      const labels = values.map(item => this.label(item.month));
      const data = values.map(item => item.spending);

      if (this.chart) {
        this.chart.data.labels = labels;
        this.chart.data.datasets[0].data = data;
        this.chart.update('none');
        return;
      }

      this.chart = new Chart(this.canvas().nativeElement, {
        type: 'bar',
        data: {
          labels,
          datasets: [{ label: 'Wydatki', data, backgroundColor: '#14765a', borderRadius: 5, maxBarThickness: 54 }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          animation: false,
          locale: 'pl-PL',
          scales: {
            y: { beginAtZero: true, ticks: { format: { style: 'currency', currency: 'PLN' } } },
            x: { grid: { display: false } }
          },
          plugins: { tooltip: { callbacks: { label: item => amountFormat.format(item.parsed.y ?? 0) } } }
        }
      });
    });
    inject(DestroyRef).onDestroy(() => this.chart?.destroy());
  }

  label(month: string): string {
    return monthFormat.format(new Date(month + '-01T00:00:00Z'));
  }
}
