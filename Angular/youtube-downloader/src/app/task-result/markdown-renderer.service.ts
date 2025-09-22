import { Injectable } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { marked } from 'marked';
import * as katex from 'katex';

@Injectable({
  providedIn: 'root'
})
export class MarkdownRendererService1 {
  constructor(private sanitizer: DomSanitizer) {}

  /**
   * Renders Markdown content with embedded LaTeX math expressions.
   * Supports inline and display math using $, $$, \(...\), and \[...\] delimiters.
   *
   * @param content The Markdown content to process.
   * @returns The HTML string with rendered math expressions.
   */
  renderMath(content: string): string {
    
    content = content.replace(/\\\$/g, '$$$$$$');

    // Handle display math enclosed in $$...$$
    let rendered = content.replace(/\$\$([\s\S]+?)\$\$/g, (_, equation) => {
      try {
        return `<div class="katex-block">${katex.renderToString(equation, {
          throwOnError: false,
          displayMode: true
        })}</div>`;
      } catch (error) {
        console.error('KaTeX rendering error (display math $$...$$):', error);
        return `<div class="katex-error">${equation}</div>`; // Optional: style errors
      }
    });

    // Handle display math enclosed in \[...\]
    rendered = rendered.replace(/\\\[([\s\S]+?)\\\]/g, (_, equation) => {
      try {
        return `<div class="katex-block">${katex.renderToString(equation, {
          throwOnError: false,
          displayMode: true
        })}</div>`;
      } catch (error) {
        console.error('KaTeX rendering error (display math \\[...\\]):', error);
        return `<div class="katex-error">${equation}</div>`;
      }
    });

    
    rendered = rendered.replace(/(?<!\\)\$(?!\$)([\s\S]+?)(?<!\\)\$(?!\$)/g, (_, equation) => {
      const trimmedEquation = equation.trim();

      
      if (/^\d/.test(trimmedEquation) || /^[0-9.,+\-*/^() ]+$/.test(trimmedEquation)) {
      
        return `$${equation}$`;
      }

      try {
        return `<span class="katex-inline">${katex.renderToString(equation, {
          throwOnError: false,
          displayMode: false
        })}</span>`;
      } catch (error) {
        console.error('KaTeX rendering error (inline math $...$):', error);
        return `<span class="katex-error">${equation}</span>`;
      }
    });

    // Handle inline math enclosed in \(...\)
    rendered = rendered.replace(/\\\(([\s\S]+?)\\\)/g, (_, equation) => {
      try {
        return `<span class="katex-inline">${katex.renderToString(equation, {
          throwOnError: false,
          displayMode: false
        })}</span>`;
      } catch (error) {
        console.error('KaTeX rendering error (inline math \\(...\\)):', error);
        return `<span class="katex-error">${equation}</span>`;
      }
    });

    
    rendered = rendered.replace(/\$\$\$\$\$\$/g, '\\$');

    // Parse the Markdown to HTML
    const html = marked.parse(rendered);

    return html.toString();
  }
}
