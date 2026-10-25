import { useForm } from 'react-hook-form'
import { LoaderCircle, UnlockKeyhole } from 'lucide-react'
import { validId } from '../api/client'
import type { RestrictionValues } from '../lib/workspace'
import { Dialog } from './Dialog'

export function RestrictionsDialog({ values, error, busy, onClose, onSubmit }: {
    values: RestrictionValues; error: string; busy: boolean;
    onClose: () => void; onSubmit: (values: RestrictionValues) => Promise<void>
}) {
    const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<RestrictionValues>({ defaultValues: values })
    const locked = busy || isSubmitting

    return <Dialog title="Remove restrictions" onClose={onClose} busy={locked}>
        <form onSubmit={handleSubmit(onSubmit)} className="dialog-form" noValidate>
            <label htmlFor="restore-chat">Chat ID</label>
            <input id="restore-chat" required disabled={locked} spellCheck={false}
                aria-invalid={!!errors.chatId} aria-describedby={errors.chatId ? 'restore-chat-error' : undefined}
                {...register('chatId', {
                    setValueAs: (value: string) => value.trim(),
                    validate: value => validId(value) || 'Enter a valid chat ID.',
                })} />
            {errors.chatId && <p id="restore-chat-error" className="form-error" role="alert">{errors.chatId.message}</p>}
            <label htmlFor="restore-user">User ID</label>
            <input id="restore-user" required disabled={locked} spellCheck={false}
                aria-invalid={!!errors.userId} aria-describedby={errors.userId ? 'restore-user-error' : undefined}
                {...register('userId', {
                    setValueAs: (value: string) => value.trim(),
                    validate: value => validId(value, true) || 'Enter a positive user ID.',
                })} />
            {errors.userId && <p id="restore-user-error" className="form-error" role="alert">{errors.userId.message}</p>}
            {error && <p className="form-error" role="alert">{error}</p>}
            <div className="dialog-actions"><button type="button" className="button subtle" disabled={locked} onClick={onClose}>Cancel</button>
                <button type="submit" className="button primary" disabled={locked}>
                    {locked ? <LoaderCircle size={16} className="spin" /> : <UnlockKeyhole size={16} />}Remove restrictions</button></div>
        </form>
    </Dialog>
}
