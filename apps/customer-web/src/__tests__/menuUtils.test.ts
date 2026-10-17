import { describe, it, expect } from 'vitest';
import {
  normalizeTurkishText,
  sanitizeImageUrl,
  formatCurrency,
} from '../utils/menuUtils';

describe('menuUtils helper suite', () => {
  describe('normalizeTurkishText', () => {
    it('normalizes Turkish characters correctly for case-insensitive matching', () => {
      expect(normalizeTurkishText('Çıtır Tavuk')).toBe('citir tavuk');
      expect(normalizeTurkishText('İÇECEK')).toBe('icecek');
      expect(normalizeTurkishText('Ispanaklı Börek')).toBe('ispanakli borek');
      expect(normalizeTurkishText('Şiş Kebap')).toBe('sis kebap');
      expect(normalizeTurkishText('Yoğurtlu Mantı')).toBe('yogurtlu manti');
      expect(normalizeTurkishText('Sütlü Tatlı')).toBe('sutlu tatli');
    });

    it('handles empty and nullish text gracefully', () => {
      expect(normalizeTurkishText('')).toBe('');
      // @ts-expect-error test runtime resilience
      expect(normalizeTurkishText(null)).toBe('');
    });
  });

  describe('sanitizeImageUrl security policy', () => {
    it('allows valid https URLs', () => {
      const validUrl = 'https://cdn.restaurant.com/images/kebab.jpg';
      expect(sanitizeImageUrl(validUrl)).toBe(validUrl);
    });

    it('allows safe relative URLs starting with a single slash', () => {
      expect(sanitizeImageUrl('/assets/images/burger.png')).toBe('/assets/images/burger.png');
    });

    it('strictly blocks javascript: URI schemes', () => {
      expect(sanitizeImageUrl('javascript:alert(document.cookie)')).toBeNull();
      expect(sanitizeImageUrl('JAVASCRIPT:alert(1)')).toBeNull();
    });

    it('strictly blocks data: URI schemes', () => {
      expect(sanitizeImageUrl('data:text/html,<script>alert(1)</script>')).toBeNull();
    });

    it('strictly blocks protocol-relative URLs', () => {
      expect(sanitizeImageUrl('//attacker.com/evil.jpg')).toBeNull();
    });

    it('blocks other dangerous or unsupported protocols', () => {
      expect(sanitizeImageUrl('file:///etc/passwd')).toBeNull();
      expect(sanitizeImageUrl('vbscript:msgbox(1)')).toBeNull();
      expect(sanitizeImageUrl('http://insecure.com/item.jpg')).toBeNull();
    });

    it('handles empty, whitespace, and null inputs', () => {
      expect(sanitizeImageUrl('')).toBeNull();
      expect(sanitizeImageUrl('   ')).toBeNull();
      expect(sanitizeImageUrl(null)).toBeNull();
      expect(sanitizeImageUrl(undefined)).toBeNull();
    });
  });

  describe('formatCurrency', () => {
    it('correctly converts minor units to formatted currency string', () => {
      const formatted = formatCurrency(25000, 'TRY', 'tr-TR');
      // Should contain 250,00 and ₺
      expect(formatted).toMatch(/250[,.]00/);
      expect(formatted).toMatch(/₺|TRY/);
    });

    it('handles zero minor units', () => {
      const formatted = formatCurrency(0, 'TRY', 'tr-TR');
      expect(formatted).toMatch(/0[,.]00/);
    });

    it('handles negative or invalid numbers gracefully', () => {
      // @ts-expect-error runtime resilience test
      expect(formatCurrency(null)).toBe('0,00 ₺');
    });
  });
});
