import { Injectable } from '@angular/core';
import { Router } from '@angular/router';

type ReaderKind = 'fb2' | 'djvu';

@Injectable({ providedIn: 'root' })
export class ReaderFileTransferService {
  private pending?: { file: File; target: ReaderKind };
  private pendingConversion?: File;

  constructor(private router: Router) {}

  redirectIfNeeded(file: File, current: ReaderKind): boolean {
    const target: ReaderKind | undefined = /\.fb2$/i.test(file.name) ? 'fb2'
      : /\.djvu?$/i.test(file.name) ? 'djvu' : undefined;
    if (!target || target === current) return false;

    const pending = { file, target };
    this.pending = pending;
    void this.router.navigateByUrl(`/${target}-reader`).then(
      succeeded => { if (!succeeded && this.pending === pending) this.pending = undefined; },
      () => { if (this.pending === pending) this.pending = undefined; }
    );
    return true;
  }

  takeFor(target: ReaderKind): File | undefined {
    if (this.pending?.target !== target) return undefined;
    const file = this.pending.file;
    this.pending = undefined;
    return file;
  }

  queueDjvuConversion(file: File): void {
    this.pendingConversion = file;
    void this.router.navigateByUrl('/djvu-conversions');
  }

  takeForConversion(): File | undefined {
    const file = this.pendingConversion;
    this.pendingConversion = undefined;
    return file;
  }
}
