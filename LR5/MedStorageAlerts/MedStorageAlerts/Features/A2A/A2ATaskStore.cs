using System.Collections.Concurrent;

namespace MedStorage.Alerts.Api.Features.A2A;

// In-memory task store backing tasks/get. Enough for a single Alerts instance in this lab;
// a scaled-out agent would keep tasks in the database instead.
public class A2ATaskStore
{
    private readonly ConcurrentDictionary<string, A2ATask> _tasks = new();

    public void Save(A2ATask task) => _tasks[task.Id] = task;

    public A2ATask? Get(string id) => _tasks.GetValueOrDefault(id);
}
