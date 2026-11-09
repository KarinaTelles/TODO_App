<script setup lang="ts">
import { computed } from "vue";
import type { PendingAction } from "../api/types";

const props = defineProps<{ action: PendingAction; busy?: boolean }>();
defineEmits<{ confirm: [id: string]; reject: [id: string] }>();

const toolLabels: Record<string, string> = {
  create_task: "Criar tarefa",
  update_task: "Alterar tarefa",
  complete_task: "Concluir tarefa",
  delete_task: "Excluir tarefas",
};

const statusLabels: Record<string, string> = {
  pending: "Aguardando sua decisão",
  executed: "Confirmada e executada",
  rejected: "Recusada, nada foi feito",
  expired: "Expirou, nada foi feito",
  stale: "A tarefa mudou depois da proposta, nada foi feito",
  failed: "Falhou na conferência e foi desfeita",
};

const title = computed(() => toolLabels[props.action.tool] ?? props.action.tool);
const isPending = computed(() => props.action.status === "pending");
const expiresAt = computed(() =>
  new Date(props.action.expiresAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }),
);
const headingId = computed(() => `action-${props.action.id}-title`);
</script>

<template>
  <section
    class="action-card"
    :class="`status-${action.status}`"
    :aria-labelledby="headingId"
    data-testid="action-card"
  >
    <h3 :id="headingId">{{ title }}</h3>
    <!-- Preview and titles come from the server and contain user text: always interpolated, never v-html. -->
    <p class="preview" data-testid="action-preview">{{ action.preview }}</p>
    <ul v-if="action.targets.length > 0" class="targets">
      <li v-for="t in action.targets" :key="t.id">#{{ t.id }} {{ t.title }}</li>
    </ul>

    <div v-if="isPending" class="actions">
      <button type="button" class="primary" :disabled="busy" @click="$emit('confirm', action.id)">
        Confirmar
      </button>
      <button type="button" :disabled="busy" @click="$emit('reject', action.id)">Recusar</button>
      <span class="expires">Expira às {{ expiresAt }}</span>
    </div>
    <p v-else class="status" role="status" data-testid="action-status">
      {{ statusLabels[action.status] ?? action.status }}
    </p>
  </section>
</template>

<style scoped>
.action-card {
  border: 1px solid var(--border);
  border-left: 4px solid var(--accent);
  border-radius: 6px;
  padding: 12px 14px;
  background: var(--surface);
  margin: 8px 0;
}
.action-card h3 {
  margin: 0 0 6px;
  font-size: 0.95rem;
}
.preview {
  margin: 0 0 8px;
  white-space: pre-wrap;
}
.targets {
  margin: 0 0 8px;
  padding-left: 18px;
  font-size: 0.9rem;
  color: var(--muted);
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.expires {
  font-size: 0.85rem;
  color: var(--muted);
}
.status {
  margin: 0;
  font-size: 0.9rem;
  color: var(--muted);
}
.status-executed {
  border-left-color: var(--ok);
}
.status-rejected,
.status-expired,
.status-stale,
.status-failed {
  border-left-color: var(--muted);
}
</style>
