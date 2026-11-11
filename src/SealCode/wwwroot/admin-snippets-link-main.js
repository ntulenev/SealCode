fetch('/admin/snippets/manage/access', { cache: 'no-store' }).then(async response => {
  if (!response.ok || !(await response.json()).CanEdit) return;
  const navigation = document.querySelector('.admin-nav');
  if (!navigation) return;
  const link = document.createElement('a');
  link.href = '/admin/snippets/manage';
  link.textContent = 'Edit snippets';
  navigation.append(link);
}).catch(() => {});

fetch('/admin/me', { cache: 'no-store' }).then(async response => {
  if (!response.ok) return;
  const admin = await response.json();
  if (!admin.IsSuperAdmin || !admin.StatisticsEnabled) return;
  const navigation = document.querySelector('.admin-nav');
  if (!navigation || navigation.querySelector('a[href="/admin/statistics"]')) return;
  const link = document.createElement('a');
  link.href = '/admin/statistics';
  link.textContent = 'Statistics';
  navigation.append(link);
}).catch(() => {});
