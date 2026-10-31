import { Check, UnlockKeyhole } from 'lucide-react'
import type { ReviewMessage } from '../api/client'
import { reviewFromLabel } from '../lib/workspace'
import type { Decision } from '../lib/workspace'

export function LastReview({ decision, restored, busy, onRestrictions }: {
    decision: Decision; restored: boolean; busy: boolean; onRestrictions: (message: ReviewMessage) => void
}) {
    return <section className="recent-review" aria-label="Last review"><span className="recent-icon"><Check size={17} /></span>
        <div><span className="section-title">Last decision</span><p>{reviewFromLabel(decision.message.from)}<span className="recent-divider">/</span><strong>{decision.isSpam ? 'Spam' : 'Not spam'}</strong><span className="recent-divider">/</span><code>#{decision.message.messageId}</code></p></div>
        {restored ? <span className="restored-label"><Check size={15} />Restrictions removed</span>
            : <button className="button subtle" disabled={busy} onClick={() => onRestrictions(decision.message)}><UnlockKeyhole size={16} />Remove restrictions</button>}
    </section>
}
