interface TelegramWebApp {
    initData: string
    ready: () => void
    expand: () => void
    close: () => void
}

export function getTelegramWebApp(): TelegramWebApp | undefined {
    return (window as Window & { Telegram?: { WebApp?: TelegramWebApp } }).Telegram?.WebApp
}