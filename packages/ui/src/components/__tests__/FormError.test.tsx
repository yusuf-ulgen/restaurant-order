import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormError } from '../FormError';

describe('FormError Component', () => {
  it('renders error message with role="alert" and id', () => {
    render(<FormError id="email-error">Geçerli bir e-posta adresi girin.</FormError>);
    const alert = screen.getByRole('alert');

    expect(alert.id).toBe('email-error');
    expect(alert.textContent).toBe('Geçerli bir e-posta adresi girin.');
  });

  it('renders nothing when children is empty/undefined', () => {
    const { container } = render(<FormError>{null}</FormError>);
    expect(container.firstChild).toBeNull();
  });
});
