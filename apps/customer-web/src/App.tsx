import React from 'react';
import { ErrorBoundary } from '@restaurant-order/ui';
import { CustomerSessionProvider } from './session/CustomerSessionContext';
import { CustomerAppShell, CustomerAppShellProps } from './shell/CustomerAppShell';
import type { CustomerSessionDto } from '@restaurant-order/contracts';

export type CustomerAppProps = CustomerAppShellProps & {
  initialSession?: CustomerSessionDto | null;
  skipBootstrap?: boolean;
};

export const CustomerContent: React.FC<CustomerAppProps> = (props) => {
  return <CustomerAppShell {...props} />;
};

export const App: React.FC<CustomerAppProps> = ({
  initialSession,
  skipBootstrap,
  ...props
}) => {
  const shouldSkipBootstrap = skipBootstrap ?? Boolean(props.token || props.isQrLanding);
  return (
    <ErrorBoundary>
      <CustomerSessionProvider
        initialSession={initialSession}
        skipBootstrap={shouldSkipBootstrap}
      >
        <CustomerAppShell {...props} />
      </CustomerSessionProvider>
    </ErrorBoundary>
  );
};

export default App;
