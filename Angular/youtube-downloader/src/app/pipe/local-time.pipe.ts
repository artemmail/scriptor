import { Pipe, PipeTransform } from '@angular/core';
import { DatePipe } from '@angular/common';

@Pipe({
  name: 'localTime'
})
export class LocalTimePipe implements PipeTransform {
  transform(value: any, format: string = 'yyyy-MM-dd HH:mm:ss'): string | null {
    if (!value) {
      return null;
    }
    
    let date: Date;
    
    if (typeof value === 'string') {
      // Если строка не содержит символа 'T', заменяем первый пробел на 'T'
      let isoValue = value;
      if (!isoValue.includes('T')) {
        isoValue = isoValue.replace(' ', 'T');
      }
      // Если отсутствует информация о часовом поясе (нет 'Z' и знака '+'), добавляем 'Z', чтобы указать, что время в UTC
      if (!isoValue.endsWith('Z') && isoValue.indexOf('+') === -1) {
        isoValue += 'Z';
      }
      date = new Date(isoValue);
    } else {
      date = new Date(value);
    }
    
    // DatePipe по умолчанию выводит дату с учетом локального часового пояса
    return new DatePipe('en-US').transform(date, format);
  }
}


/** Умное форматирование размера файла */
@Pipe({
  name: 'fileSize',
  standalone: true,
  pure: true
})
export class FileSizePipe implements PipeTransform {
  transform(value?: number | null): string {
    if (value == null || isNaN(value as any)) return '—';
    const n = Number(value);
    if (n < 1024) return `${n} B`;
    const kb = n / 1024;
    if (kb < 1024) return `${kb.toFixed(kb < 10 ? 1 : 0)} KB`;
    const mb = kb / 1024;
    if (mb < 1024) return `${mb.toFixed(mb < 10 ? 1 : 0)} MB`;
    const gb = mb / 1024;
    return `${gb.toFixed(gb < 10 ? 1 : 0)} GB`;
  }
}

/* ...остальные импорты... */

/** Умное форматирование битрейта */
@Pipe({
  name: 'bitrate',
  standalone: true,
  pure: true
})
export class BitratePipe implements PipeTransform {
  transform(value: number | string | null | undefined): string {
    if (value == null) return '—';

    // Если число — считаем что это bps
    let bps: number | null = null;

    if (typeof value === 'number') {
      bps = value;
    } else if (typeof value === 'string') {
      const lower = value.trim().toLowerCase();

      // определим множитель по единице измерения (bps/kbps/mbps/gbps)
      let factor = 1; // bps по умолчанию
      if (/\bgbps?\b/.test(lower)) factor = 1_000_000_000;
      else if (/\bmbps?\b/.test(lower)) factor = 1_000_000;
      else if (/\bkbps?\b|\bkbit\/s\b|\skb\/s\b/.test(lower)) factor = 1_000;

      // извлечь число (поддержка , и .)
      const m = lower.match(/([\d.,]+)/);
      if (!m) return value;

      let numStr = m[1];

      // Если только запятые — вероятно это разделители тысяч: уберём их
      if (numStr.includes(',') && !numStr.includes('.')) {
        numStr = numStr.replace(/,/g, '');
      } else {
        // иначе запятую считаем десятичным разделителем
        numStr = numStr.replace(/,/g, '.');
        // и лишние точки уберём (оставим первую)
        const firstDot = numStr.indexOf('.');
        if (firstDot !== -1) {
          numStr = numStr.slice(0, firstDot + 1) + numStr.slice(firstDot + 1).replace(/\./g, '');
        }
      }

      const num = parseFloat(numStr);
      if (!isNaN(num)) bps = num * factor;
    }

    if (bps == null || isNaN(bps)) return '—';

    // Форматирование единиц
    if (bps < 1000) return `${Math.round(bps)} bps`;

    const kbps = bps / 1000;
    if (kbps < 1000) return `${kbps < 10 ? kbps.toFixed(1) : Math.round(kbps)} kbps`;

    const mbps = kbps / 1000;
    if (mbps < 1000) return `${mbps < 10 ? mbps.toFixed(1) : Math.round(mbps)} Mbps`;

    const gbps = mbps / 1000;
    return `${gbps < 10 ? gbps.toFixed(1) : Math.round(gbps)} Gbps`;
  }
}