import { describe, expect, it, vi } from "vitest";
import { ApiError, createClient } from "../src/api/client";

function jsonResponse(status: number, body: unknown, contentType = "application/json") {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { "Content-Type": contentType },
  });
}

describe("api client", () => {
  it("sends JSON to the right path and returns the parsed body", async () => {
    const fetchFn = vi.fn().mockResolvedValue(jsonResponse(201, { id: 1, title: "Pagar boleto" }));
    const client = createClient("/api", fetchFn);

    const task = await client.createTask({ title: "Pagar boleto", dueDate: "2026-11-10" });

    expect(task).toMatchObject({ id: 1, title: "Pagar boleto" });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toBe("/api/tasks");
    expect(init.method).toBe("POST");
    expect(init.headers["Content-Type"]).toBe("application/json");
    expect(JSON.parse(init.body)).toEqual({ title: "Pagar boleto", dueDate: "2026-11-10" });
  });

  it("sends the version when deleting and accepts 204", async () => {
    const fetchFn = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const client = createClient("/api", fetchFn);

    await expect(client.deleteTask(7, 3)).resolves.toBeUndefined();
    expect(fetchFn.mock.calls[0][0]).toBe("/api/tasks/7?version=3");
    expect(fetchFn.mock.calls[0][1].method).toBe("DELETE");
  });

  it("posts chat with the conversation id", async () => {
    const fetchFn = vi.fn().mockResolvedValue(
      jsonResponse(200, { conversationId: "c1", mode: "demo", messages: [], steps: [], pendingActions: [] }),
    );
    const client = createClient("/api", fetchFn);

    await client.chat("listar", "c1");

    expect(fetchFn.mock.calls[0][0]).toBe("/api/chat");
    expect(JSON.parse(fetchFn.mock.calls[0][1].body)).toEqual({ conversationId: "c1", message: "listar" });
  });

  it("confirms and rejects actions without a body", async () => {
    const fetchFn = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(200, { action: {}, chat: null })));
    const client = createClient("/api", fetchFn);

    await client.confirmAction("a/1");
    await client.rejectAction("a2");

    expect(fetchFn.mock.calls[0][0]).toBe("/api/actions/a%2F1/confirm");
    expect(fetchFn.mock.calls[0][1].body).toBeUndefined();
    expect(fetchFn.mock.calls[1][0]).toBe("/api/actions/a2/reject");
  });

  it("turns ProblemDetails into ApiError and keeps the body", async () => {
    const problem = {
      title: "Ação já resolvida",
      status: 409,
      detail: "Esta ação já está com situação 'executed'. Nada foi feito.",
      action: { id: "a1", status: "executed" },
    };
    const fetchFn = vi.fn().mockResolvedValue(jsonResponse(409, problem, "application/problem+json"));
    const client = createClient("/api", fetchFn);

    const error = await client.confirmAction("a1").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).message).toBe(problem.detail);
    expect((error as ApiError).problem.action?.status).toBe("executed");
  });

  it("uses validation messages when present", async () => {
    const fetchFn = vi.fn().mockResolvedValue(
      jsonResponse(400, { title: "One or more validation errors occurred.", errors: { title: ["O título é obrigatório."] } }),
    );
    const client = createClient("/api", fetchFn);

    await expect(client.createTask({ title: "" })).rejects.toThrow("O título é obrigatório.");
  });

  it("reports network failures as status 0", async () => {
    const fetchFn = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
    const client = createClient("/api", fetchFn);

    const error = await client.info().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(0);
  });
});
