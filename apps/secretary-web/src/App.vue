<template>
  <div class="layout">
    <header class="topbar">
      <h1>AI Secretary Console</h1>
      <nav>
        <RouterLink to="/">任务看板</RouterLink>
        <RouterLink to="/terminal">终端工作台</RouterLink>
      </nav>
    </header>
    <main class="content">
      <RouterView />
    </main>
  </div>
</template>
