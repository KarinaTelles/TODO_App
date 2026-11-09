<script setup lang="ts">
import { reactive, ref } from "vue";
import type { Priority, Task, TaskUpdate } from "../api/types";

const props = defineProps<{ task: Task }>();
const emit = defineEmits<{
  toggle: [task: Task];
  save: [task: Task, update: TaskUpdate];
  remove: [task: Task];
}>();

const editing = ref(false);
const form = reactive({ title: "", notes: "", dueDate: "", priority: "normal" as Priority });

const priorityLabels: Record<Priority, string> = { low: "baixa", normal: "normal", high: "alta" };

function startEdit() {
  form.title = props.task.title;
  form.notes = props.task.notes ?? "";
  form.dueDate = props.task.dueDate ?? "";
  form.priority = props.task.priority;
  editing.value = true;
}

function save() {
  if (!form.title.trim()) return;
  emit("save", props.task, {
    title: form.title.trim(),
    notes: form.notes.trim() || null,
    dueDate: form.dueDate || null,
    priority: form.priority,
    done: props.task.done,
    version: props.task.version,
  });
  editing.value = false;
}

function formatDate(iso: string) {
  const [y, m, d] = iso.split("-");
  return `${d}/${m}/${y}`;
}
</script>

<template>
  <li class="task" :class="{ done: task.done }" data-testid="task-item">
    <form v-if="editing" class="edit" @submit.prevent="save">
      <label>Título <input v-model="form.title" required maxlength="200" /></label>
      <label>Notas <textarea v-model="form.notes" rows="2" maxlength="2000"></textarea></label>
      <div class="row">
        <label>Prazo <input v-model="form.dueDate" type="date" /></label>
        <label>
          Prioridade
          <select v-model="form.priority">
            <option value="low">baixa</option>
            <option value="normal">normal</option>
            <option value="high">alta</option>
          </select>
        </label>
      </div>
      <div class="row">
        <button type="submit" class="primary">Salvar</button>
        <button type="button" @click="editing = false">Cancelar</button>
      </div>
    </form>

    <template v-else>
      <label class="check">
        <input type="checkbox" :checked="task.done" @change="emit('toggle', task)" />
        <span class="title">#{{ task.id }} {{ task.title }}</span>
      </label>
      <div class="meta">
        <span v-if="task.dueDate">prazo {{ formatDate(task.dueDate) }}</span>
        <span v-if="task.priority !== 'normal'">prioridade {{ priorityLabels[task.priority] }}</span>
      </div>
      <p v-if="task.notes" class="notes">{{ task.notes }}</p>
      <div class="row buttons">
        <button type="button" class="link" @click="startEdit">Editar<span class="visually-hidden"> {{ task.title }}</span></button>
        <button type="button" class="link danger" @click="emit('remove', task)">Excluir<span class="visually-hidden"> {{ task.title }}</span></button>
      </div>
    </template>
  </li>
</template>

<style scoped>
.task {
  padding: 10px 0;
  border-bottom: 1px solid var(--border);
}
.check {
  display: flex;
  gap: 8px;
  align-items: flex-start;
}
.done .title {
  text-decoration: line-through;
  color: var(--muted);
}
.meta {
  display: flex;
  gap: 12px;
  margin-left: 26px;
  font-size: 0.85rem;
  color: var(--muted);
}
.notes {
  margin: 4px 0 0 26px;
  font-size: 0.9rem;
  white-space: pre-wrap;
}
.buttons {
  margin-left: 26px;
}
.edit {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.edit label {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 0.85rem;
}
.row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
</style>
