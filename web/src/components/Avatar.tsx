import { Users } from "lucide-react";
import { initials } from "../format";

/** Initials on a neutral disc; a small dot when the person is online. Groups get an icon. */
export function Avatar({ name, online, group, size = 40 }: { name: string; online?: boolean; group?: boolean; size?: number }) {
  return (
    <span className="avatar" style={{ width: size, height: size }} aria-hidden>
      {group ? <Users size={size * 0.45} /> : <span style={{ fontSize: size * 0.36 }}>{initials(name)}</span>}
      {online && <span className="online-dot" />}
    </span>
  );
}
