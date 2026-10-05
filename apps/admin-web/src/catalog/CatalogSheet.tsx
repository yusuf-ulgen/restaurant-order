import React, { useEffect, useState } from 'react';
import { BottomSheet, Modal } from '@restaurant-order/ui';

export const CatalogSheet: React.FC<{ isOpen: boolean; title: string; onClose: () => void; children: React.ReactNode }> = ({ isOpen, title, onClose, children }) => {
  const [mobile, setMobile] = useState(false);
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const query = window.matchMedia('(max-width: 700px)');
    const update = () => setMobile(query.matches);
    update();
    query.addEventListener?.('change', update);
    return () => query.removeEventListener?.('change', update);
  }, []);
  return mobile
    ? <BottomSheet isOpen={isOpen} title={title} onClose={onClose}>{children}</BottomSheet>
    : <Modal isOpen={isOpen} title={title} size="lg" onClose={onClose}>{children}</Modal>;
};
