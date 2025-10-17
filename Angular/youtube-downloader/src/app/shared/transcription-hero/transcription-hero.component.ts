import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-transcription-hero',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './transcription-hero.component.html',
  styleUrls: ['./transcription-hero.component.css'],
  host: {
    '[class.hero-full-width]': 'fullWidth',
  },
})
export class TranscriptionHeroComponent {
  @Input() title = '';
  @Input() lead = '';
  @Input() highlights: readonly string[] = [];
  @Input() showSubscriptionStatus = false;
  @Input() summaryLoading = false;
  @Input() subscriptionChipClass = 'status-neutral';
  @Input() subscriptionStatusMessage = '';
  @Input() subscriptionLoadingLabel = 'Загружаем статус подписки…';
  @Input() billingButtonLabel = 'Перейти к тарифам';
  @Input() fullWidth = false;
  @Input() cardless = false;

  @Output() billingClick = new EventEmitter<void>();

  onBillingClick(): void {
    this.billingClick.emit();
  }
}
