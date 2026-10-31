const time = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });
const day = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long" });
const short = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short" });

export const formatTime = (iso: string) => time.format(new Date(iso));

export function formatDay(iso: string): string {
  const date = new Date(iso);
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  if (date.toDateString() === today.toDateString()) return "Today";
  if (date.toDateString() === yesterday.toDateString()) return "Yesterday";
  return day.format(date);
}

/** In the conversation list: the time today, otherwise the date. */
export function formatListTime(iso: string): string {
  const date = new Date(iso);
  return date.toDateString() === new Date().toDateString() ? time.format(date) : short.format(date);
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toLocaleUpperCase("tr-TR");
}
