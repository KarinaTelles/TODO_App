<script setup lang="ts">
import { computed, nextTick, ref } from "vue";
import { api as defaultApi, ApiError, type ApiClient } from "../api/client";
import type { ChatResponse, PendingAction } from "../api/types";
import ActionCard from "./ActionCard.vue";

const props = defineProps<{ api?: ApiClient; demoMode: boolean }>();
const emit = defineEmits<{ "tasks-changed": [] }>();
const client = computed(() => props.api ?? defaultApi);

type Entry =
  | { kind: "message"; key: number; role: "user" | "assistant"; text: string }
  | { kind: "action"; key: number; action: PendingAction };

const entries = ref<Entry[]>([]);
const conversationId = ref<string | null>(null);
const draft = ref("");
const sending = ref(false);
const busyAction = ref<string | null>(null);
const error = ref("");
const log = ref<HTMLElement | null>(null);
let nextKey = 0;

const hasPending = computed(() =>
  entries.value.some((e) => e.kind === "action" && e.action.status === "pending"),
);

async function scrollToEnd() {
  await nextTick();
  log.value?.scrollTo?.({ top: log.value.scrollHeight });
}

function push(response: ChatResponse) {
  conversationId.value = response.conversationId;
  for (const m of response.messages) {
    entries.value.push({ kind: "message", key: nextKey++, role: m.role, text: m.text });
  }
  for (const a of response.pendingActions) {
    entries.value.push({ kind: "action", key: nextKey++, action: a });
  }
  void scrollToEnd();
}

function replaceAction(action: PendingAction) {
  const entry = entries.value.find((e) => e.kind === "action" && e.action.id === action.id);
  if (entry && entry.kind === "action") entry.action = action;
}

async function send() {
  const message = draft.value.trim();
  if (!message || sending.value) return;
  error.value = "";
  sending.value = true;
  entries.value.push({ kind: "message", key: nextKey++, role: "user", text: message });
  draft.value = "";
  void scrollToEnd();
  try {
    push(await client.value.chat(message, conversationId.value));
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Erro inesperado.";
  } finally {
    sending.value = false;
  }
}

async function resolve(id: string, decision: "confirm" | "reject") {
  error.value = "";
  busyAction.value = id;
  try {
    const result =
      decision === "confirm"
        ? await client.value.confirmAction(id)
        : await client.value.rejectAction(id);
    replaceAction(result.action);
    if (result.action.status === "executed") emit("tasks-changed");
    if (result.chat) push(result.chat);
  } catch (e) {
    // 409: someone already resolved it (another tab, double click). Show the real state.
    if (e instanceof ApiError && e.status === 409 && e.problem.action) {
      replaceAction(e.problem.action);
      emit("tasks-changed");
    }
    error.value = e instanceof ApiError ? e.message : "Erro inesperado.";
  } finally {
    busyAction.value = null;
  }
}

function newConversation() {
  entries.value = [];
  conversationId.value = null;
  error.value = "";
}
</script>

<template>
  <section class="chat" aria-labelledby="chat-title">
    <div class="chat-head">
      <h2 id="chat-title">Assistente</h2>
      <button type="button" class="link" :disabled="sending" @click="newConversation">Nova conversa</button>
    </div>

    <p v-if="demoMode" class="hint">
      Modo demonstração: sem IA. Exemplos: <code>listar</code>, <code>criar "pagar boleto" para amanhã</code>,
      <code>concluir 3</code>, <code>adiar 3 para 20/11</code>, <code>excluir 3</code>.
    </p>

    <div ref="log" class="log" role="log" aria-live="polite" data-testid="chat-log">
      <p v-if="entries.length === 0" class="empty">
        Peça para listar, criar, alterar, concluir ou excluir tarefas. Nada é alterado sem a sua confirmação.
      </p>
      <template v-for="entry in entries" :key="entry.key">
        <p v-if="entry.kind === 'message'" class="msg" :class="entry.role">
          <span class="who">{{ entry.role === "user" ? "Você" : "Assistente" }}</span>
          {{ entry.text }}
        </p>
        <ActionCard
          v-else
          :action="entry.action"
          :busy="busyAction === entry.action.id"
          @confirm="resolve($event, 'confirm')"
          @reject="resolve($event, 'reject')"
        />
      </template>
      <p v-if="sending" class="msg assistant muted">Pensando...</p>
    </div>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <form class="composer" @submit.prevent="send">
      <label for="chat-input" class="visually-hidden">Mensagem para o assistente</label>
      <input
        id="chat-input"
        v-model="draft"
        maxlength="4000"
        autocomplete="off"
        :disabled="sending || hasPending"
        :placeholder="hasPending ? 'Confirme ou recuse a proposta acima' : 'Escreva uma mensagem'"
      />
      <button type="submit" :disabled="sending || hasPending || !draft.trim()">Enviar</button>
    </form>
  </section>
</template>

<style scoped>
.chat {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.chat-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.hint {
  font-size: 0.85rem;
  color: var(--muted);
  margin: 0 0 8px;
}
.log {
  flex: 1;
  overflow-y: auto;
  max-height: 60vh;
  min-height: 200px;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  background: var(--bg);
}
.empty {
  color: var(--muted);
}
.msg {
  margin: 8px 0;
  white-space: pre-wrap;
}
.msg.user {
  text-align: right;
}
.who {
  display: block;
  font-size: 0.75rem;
  color: var(--muted);
}
.muted {
  color: var(--muted);
}
.composer {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}
.composer input {
  flex: 1;
}
</style>
