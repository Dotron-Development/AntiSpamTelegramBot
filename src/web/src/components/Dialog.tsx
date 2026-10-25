import { useEffect, useId, useRef } from 'react'
import type { ReactNode } from 'react'
import { X } from 'lucide-react'

export function Dialog({ title, children, onClose, busy = false }: {
    title: string; children: ReactNode; onClose: () => void; busy?: boolean
}) {
    const element = useRef<HTMLDialogElement>(null)
    const titleId = useId()
    useEffect(() => {
        const dialog = element.current!
        dialog.showModal()
        return () => dialog.close()
    }, [])
    return <dialog ref={element} className="dialog" aria-labelledby={titleId}
        onCancel={event => { event.preventDefault(); if (!busy) onClose() }}>
        <header className="dialog-heading"><h2 id={titleId}>{title}</h2>
            <button className="icon-button" aria-label="Close dialog" title="Close dialog"
                onClick={onClose} disabled={busy}><X size={19} /></button>
        </header>
        {children}
    </dialog>
}
