<script setup lang="ts">
import { computed, ref } from "vue";
import { api as defaultApi, ApiError, type ApiClient } from "../api/client";
import type { Priority, Task, TaskUpdate } from "../api/types";
import TaskItem from "./TaskItem.vue";

const props = defineProps<{ api?: ApiClient }>();
const client = computed(() => props.api ?? defaultApi);

const tasks = ref<Task[]>([]);
const filter = ref<"all" | "open" | "done">("all");
const newTitle = ref("");
const newDueDate = ref("");
const newPriority = ref<Priority>("normal");
const loading = ref(false);
const error = ref("");

async function load() {
  loading.value = true;
  try {
    tasks.value = await client.value.listTasks(filter.value);
    error.value = "";
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Erro ao carregar tarefas.";
  } finally {
    loading.value = false;
  }
}

async function run(action: () => Promise<unknown>) {
  error.value = "";
  try {
    await action();
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Erro inesperado.";
    if (e instanceof ApiError && e.status === 409) {
      error.value += " A lista foi recarregada.";
    }
  }
  await load();
}

function add() {
  const title = newTitle.value.trim();
  if (!title) return;
  return run(async () => {
    await client.value.createTask({
      title,
      dueDate: newDueDate.value || null,
      priority: newPriority.value,
    });
    newTitle.value = "";
    newDueDate.value = "";
    newPriority.value = "normal";
  });
}

function toggle(t: Task) {
  return run(() =>
    client.value.updateTask(t.id, {
      title: t.title,
      notes: t.notes,
      dueDate: t.dueDate,
      priority: t.priority,
      done: !t.done,
      version: t.version,
    }),
  );
}

function save(t: Task, update: TaskUpdate) {
  return run(() => client.value.updateTask(t.id, update));
}

function remove(t: Task) {
  if (!window.confirm(`Excluir "${t.title}"?`)) return;
  return run(() => client.value.deleteTask(t.id, t.version));
}

defineExpose({ load });
void load();
</script>

<template>
  <section class="tasks" aria-labelledby="tasks-title">
    <h2 id="tasks-title">Tarefas</h2>

    <form class="add" @submit.prevent="add">
      <label for="new-title" class="visually-hidden">Nova tarefa</label>
      <input id="new-title" v-model="newTitle" placeholder="Nova tarefa" maxlength="200" />
      <label for="new-due" class="visually-hidden">Prazo</label>
      <input id="new-due" v-model="newDueDate" type="date" />
      <label for="new-priority" class="visually-hidden">Prioridade</label>
      <select id="new-priority" v-model="newPriority">
        <option value="low">baixa</option>
        <option value="normal">normal</option>
        <option value="high">alta</option>
      </select>
      <button type="submit" :disabled="!newTitle.trim()">Adicionar</button>
    </form>

    <fieldset class="filter">
      <legend class="visually-hidden">Mostrar</legend>
      <label><input v-model="filter" type="radio" value="all" @change="load" /> todas</label>
      <label><input v-model="filter" type="radio" value="open" @change="load" /> abertas</label>
      <label><input v-model="filter" type="radio" value="done" @change="load" /> concluídas</label>
    </fieldset>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <ul v-if="tasks.length" class="list" data-testid="task-list">
      <TaskItem
        v-for="t in tasks"
        :key="t.id"
        :task="t"
        @toggle="toggle"
        @save="save"
        @remove="remove"
      />
    </ul>
    <p v-else-if="!loading" class="empty">Nenhuma tarefa por aqui.</p>
  </section>
</template>

<style scoped>
.add {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.add input:first-of-type {
  flex: 1 1 200px;
}
.filter {
  border: 0;
  padding: 0;
  margin: 12px 0 0;
  display: flex;
  gap: 16px;
  font-size: 0.9rem;
}
.list {
  list-style: none;
  padding: 0;
  margin: 8px 0 0;
}
.empty {
  color: var(--muted);
}
</style>
