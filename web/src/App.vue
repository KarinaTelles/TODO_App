<script setup lang="ts">
import { onMounted, ref } from "vue";
import { api } from "./api/client";
import type { ServerInfo } from "./api/types";
import ChatPanel from "./components/ChatPanel.vue";
import TaskList from "./components/TaskList.vue";

const info = ref<ServerInfo | null>(null);
const infoError = ref(false);
const taskList = ref<InstanceType<typeof TaskList> | null>(null);

onMounted(async () => {
  try {
    info.value = await api.info();
  } catch {
    infoError.value = true;
  }
});
</script>

<template>
  <header class="top">
    <h1>Assistente de tarefas</h1>
    <p v-if="info?.demoMode" class="badge demo" data-testid="demo-badge">
      Modo demonstração: comandos fixos, sem IA
    </p>
    <p v-else-if="info" class="badge">Modelo: {{ info.model }}</p>
    <p v-else-if="infoError" class="badge error">API fora do ar</p>
  </header>

  <main class="layout">
    <TaskList ref="taskList" />
    <ChatPanel :demo-mode="info?.demoMode ?? false" @tasks-changed="taskList?.load()" />
  </main>
</template>
