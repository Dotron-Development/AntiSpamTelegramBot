import { useEffect, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Copy, LoaderCircle, Sparkles } from 'lucide-react'
import { previewMention } from '../api/client'
import type { Connection } from '../api/client'
import { errorText } from '../lib/workspace'

type Values = {
    question: string; fromName: string; fromUsername: string;
    includeReply: boolean; replyName: string; replyUsername: string; replyText: string
}

export function MentionPreview({ connection, busy, onBusyChange, onCopy }: {
    connection: Connection; busy: boolean; onBusyChange: (busy: boolean) => void;
    onCopy: (text: string) => Promise<void>
}) {
    const { register, handleSubmit, control, formState: { isSubmitting } } = useForm<Values>({
        defaultValues: { question: '', fromName: '', fromUsername: '', includeReply: false, replyName: '', replyUsername: '', replyText: '' },
    })
    const [answer, setAnswer] = useState<string | null>(null)
    const [error, setError] = useState('')
    const request = useRef<AbortController | null>(null)
    useEffect(() => () => request.current?.abort(), [])
    const locked = busy || isSubmitting
    const includeReply = useWatch({ control, name: 'includeReply' })
    const optional = (value: string) => value.trim() || undefined

    async function generate(values: Values) {
        if (busy || request.current) return
        const controller = new AbortController()
        request.current = controller
        onBusyChange(true)
        setAnswer(null)
        setError('')
        try {
            const result = await previewMention(connection, {
                question: values.question.trim(),
                fromName: optional(values.fromName), fromUsername: optional(values.fromUsername),
                ...(values.includeReply ? { replyTo: {
                    name: optional(values.replyName), username: optional(values.replyUsername), text: values.replyText,
                } } : {}),
            }, controller.signal)
            if (!controller.signal.aborted) setAnswer(result.answer)
        } catch (error) {
            if (!controller.signal.aborted) setError(errorText(error))
        } finally {
            request.current = null
            if (!controller.signal.aborted) onBusyChange(false)
        }
    }

    return <section className="mention-preview" aria-label="Mention command test">
        <p className="mention-description">Test @botname without posting to Telegram or consuming command cooldown. Only an explicit reply is used as context; chat history is not read.</p>
        <form onSubmit={event => { void handleSubmit(generate)(event) }} onChange={() => { setAnswer(null); setError('') }}>
            <fieldset className="mention-fields" disabled={locked}>
                <label htmlFor="mention-question">Question</label>
                <textarea id="mention-question" rows={4} maxLength={4096} placeholder="have any comments on that?"
                    aria-describedby="mention-question-hint" {...register('question')} />
                <p id="mention-question-hint" className="mention-hint">Enter the text after @botname. Leave empty to test a bare mention.</p>
                <div className="mention-authors">
                    <div><label htmlFor="mention-from">Question author (optional)</label>
                        <input id="mention-from" maxLength={200} placeholder="Ben" {...register('fromName')} /></div>
                    <div><label htmlFor="mention-from-username">Question author username (optional)</label>
                        <input id="mention-from-username" maxLength={64} placeholder="ben" {...register('fromUsername')} /></div>
                </div>
                <label className="mention-reply-toggle"><input type="checkbox" {...register('includeReply')} />Include Telegram reply context</label>
                {includeReply && <div className="mention-reply-fields">
                    <div className="mention-authors">
                        <div><label htmlFor="mention-reply-author">Reply author (optional)</label>
                            <input id="mention-reply-author" maxLength={200} placeholder="Alice" {...register('replyName')} /></div>
                        <div><label htmlFor="mention-reply-username">Reply author username (optional)</label>
                            <input id="mention-reply-username" maxLength={64} placeholder="alice" {...register('replyUsername')} /></div>
                    </div>
                    <label htmlFor="mention-reply-text">Reply text or caption</label>
                    <textarea id="mention-reply-text" rows={4} maxLength={4096} placeholder="Democrats won today" {...register('replyText')} />
                </div>}
                <button type="submit" className="button primary">
                    {isSubmitting ? <LoaderCircle size={16} className="spin" /> : <Sparkles size={16} />}
                    {isSubmitting ? 'Generating...' : 'Generate answer'}
                </button>
            </fieldset>
        </form>
        {error && <p className="notice error" role="alert">{error}</p>}
        {answer !== null && <section className="chronicle-preview" aria-label="Mention answer">
            <div className="chronicle-preview-heading"><div><h2>AI answer</h2><span>Markdown source; Telegram formatting is applied when the bot sends a message.</span></div>
                <button className="icon-button" title="Copy answer" aria-label="Copy answer" onClick={() => void onCopy(answer)}><Copy size={16} /></button></div>
            <div className="chronicle-preview-text">{answer}</div>
        </section>}
    </section>
}
