import type { Server } from "node:http";

export function createMockServer(): Server;
export function startMockServer(port?: number): Promise<{ url: string; close: () => Promise<void> }>;
