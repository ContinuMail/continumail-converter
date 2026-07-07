// Screenshot harness — drop-in replacement for @tauri-apps/api/event.
// A window-level bus: mock-engine emits, useConvert's listen() receives.
export type UnlistenFn = () => void;
type Handler = (event: { event: string; id: number; payload: unknown }) => void;

const handlers = new Map<string, Set<Handler>>();
let nextId = 1;

export async function listen<T>(
  event: string,
  handler: (event: { event: string; id: number; payload: T }) => void,
): Promise<UnlistenFn> {
  let set = handlers.get(event);
  if (!set) handlers.set(event, (set = new Set()));
  set.add(handler as Handler);
  return () => set!.delete(handler as Handler);
}

export function emit(event: string, payload: unknown): void {
  const set = handlers.get(event);
  if (!set) return;
  for (const h of [...set]) h({ event, id: nextId++, payload });
}
