using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class DraftWorkspaceServiceTests
{
    [Fact]
    public async Task DraftWorkspaceService_AnalyzeRunsAllWorkflowStepsInOrder()
    {
        var logger = new InMemoryWorkflowLogger();
        var store = new InMemoryInteractionProjectStore();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), new MockComfyUiClient(), store, logger);

        var response = await service.AnalyzeWithLlmAsync("陈默说：我来晚了。");

        Assert.Contains(response.Project.Scenes, scene => scene.Interactions.Count > 0);
        Assert.Same(response.Project, await store.LoadLastAsync());
        AssertOrderedWorkflow(
            logger,
            (WorkflowSteps.ScriptInput, "prepare_started"),
            (WorkflowSteps.PromptBuild, "request_started"),
            (WorkflowSteps.LlmAnalysis, "call_started"),
            (WorkflowSteps.JsonNormalize, "normalize_started"),
            (WorkflowSteps.ProjectStore, "save_started"),
            (WorkflowSteps.ProjectStore, "save_completed"));
    }

    [Fact]
    public async Task DraftWorkspaceService_EnqueueAndProcessAllTtsDrainsQueuedPromptsUntilAudioIsAvailable()
    {
        var logger = new InMemoryWorkflowLogger();
        var client = new CapturingComfyClient();
        var store = new InMemoryInteractionProjectStore();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, store, logger);
        var response = await service.AnalyzeWithLlmAsync("陈默说：我来晚了。\n林安说：我等了很久。");
        var project = WithDefaultTtsVoices(response.Project);
        var ttsTaskIds = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .Select(task => task.Id)
            .ToArray();

        var updated = await service.EnqueueAndProcessAllTtsAsync(project);

        Assert.NotEmpty(ttsTaskIds);
        Assert.Equal(ttsTaskIds, client.SubmittedTaskIds);
        Assert.Equal(ttsTaskIds.Select(id => $"prompt_{id}").ToArray(), client.StatusPromptIds);
        Assert.All(updated.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts), task =>
        {
            Assert.Equal(GenerationTaskStatus.Completed, task.Status);
            Assert.Equal($"output||prompt_{task.Id}.flac", task.ResultAssetId);
        });
        Assert.Same(updated, await store.LoadLastAsync());
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.ProjectStore && entry.Operation == "tts_progress_save_started");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.TtsQueue && entry.Operation == "drain_completed");
    }

    [Fact]
    public async Task ProcessNextTtsQueueStepAsyncKeepsOnlyTwoSubmittedPromptsAndAppendsAfterOneCompletes()
    {
        var client = new WindowedQueueComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore());
        var response = await service.AnalyzeWithLlmAsync("陈默说：一。\n林安说：二。\n陈默说：三。");
        var project = WithDefaultTtsVoices(response.Project);

        var firstPass = await service.ProcessNextTtsQueueStepAsync(project, maxInFlightPrompts: 2);

        Assert.Equal(2, client.SubmittedTaskIds.Count);
        Assert.Equal(2, firstPass.GenerationTasks.Count(task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Pending &&
            task.Parameters.ContainsKey(TtsSynthesisQueueService.PromptIdParameter)));

        var secondPass = await service.ProcessNextTtsQueueStepAsync(firstPass, maxInFlightPrompts: 2);

        Assert.Equal(2, client.SubmittedTaskIds.Count);
        Assert.Equal(2, secondPass.GenerationTasks.Count(task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Pending &&
            task.Parameters.ContainsKey(TtsSynthesisQueueService.PromptIdParameter)));

        client.CompleteNextStatusCheck = true;
        var thirdPass = await service.ProcessNextTtsQueueStepAsync(secondPass, maxInFlightPrompts: 2);

        Assert.Equal(3, client.SubmittedTaskIds.Count);
        Assert.Contains(thirdPass.GenerationTasks, task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Completed);
        Assert.Equal(2, thirdPass.GenerationTasks.Count(task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Pending &&
            task.Parameters.ContainsKey(TtsSynthesisQueueService.PromptIdParameter)));
    }

    [Fact]
    public async Task ProcessNextTtsQueueStepAsyncStopsSubmittingWhenInFlightPromptFails()
    {
        var client = new WindowedQueueComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore());
        var response = await service.AnalyzeWithLlmAsync("陈默说：一。\n林安说：二。\n陈默说：三。");
        var project = WithDefaultTtsVoices(response.Project);

        var firstPass = await service.ProcessNextTtsQueueStepAsync(project, maxInFlightPrompts: 2);
        client.FailNextStatusCheck = true;

        var secondPass = await service.ProcessNextTtsQueueStepAsync(firstPass, maxInFlightPrompts: 2);

        Assert.Equal(2, client.SubmittedTaskIds.Count);
        Assert.Contains(secondPass.GenerationTasks, task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Failed);
        Assert.Equal(1, secondPass.GenerationTasks.Count(task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Draft));
    }

    [Fact]
    public async Task ProcessNextTtsQueueStepAsyncReleasesPromptSlotWhenComfyPromptDisappears()
    {
        var client = new WindowedQueueComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore());
        var response = await service.AnalyzeWithLlmAsync("陈默说：一。\n林安说：二。\n陈默说：三。");
        var project = WithDefaultTtsVoices(response.Project);

        var firstPass = await service.ProcessNextTtsQueueStepAsync(project, maxInFlightPrompts: 2);
        var disappearingPromptId = firstPass.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .Select(task => task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId) ? promptId : null)
            .First(promptId => !string.IsNullOrWhiteSpace(promptId))!;
        client.NotFoundPromptIds.Add(disappearingPromptId);

        var secondPass = await service.ProcessNextTtsQueueStepAsync(firstPass, maxInFlightPrompts: 2);
        var thirdPass = await service.ProcessNextTtsQueueStepAsync(secondPass, maxInFlightPrompts: 2);

        Assert.Equal(3, client.SubmittedTaskIds.Count);
        Assert.Contains(thirdPass.GenerationTasks, task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Pending &&
            !task.Parameters.ContainsKey(TtsSynthesisQueueService.PromptIdParameter) &&
            task.Parameters[TtsSynthesisQueueService.QueueMessageParameter].Contains("重新提交", StringComparison.Ordinal));
        Assert.Contains(thirdPass.GenerationTasks, task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status == GenerationTaskStatus.Pending &&
            task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId) &&
            !string.Equals(promptId, disappearingPromptId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TtsQueueAllowsMissingReferenceTextForCharacterVoicesNodeSidecar()
    {
        var logger = new InMemoryWorkflowLogger();
        var client = new CapturingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore(), logger);
        var task = new GenerationTask(
            "tts_missing_reference_text",
            GenerationTaskType.Tts,
            "int_missing_reference_text",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line",
                ["voiceReferenceId"] = "custom.wav"
            });
        var project = EmptyProject() with { GenerationTasks = [task] };

        var updated = await service.EnqueueAndProcessAllTtsAsync(project);

        Assert.Single(client.SubmittedTaskIds);
        Assert.Contains(updated.GenerationTasks, candidate =>
            candidate.Id == task.Id &&
            candidate.Status == GenerationTaskStatus.Completed);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Step == WorkflowSteps.TtsQueue && entry.Operation == "task_submit_failed");
    }

    [Fact]
    public async Task ResubmitTtsTaskAsync_OverridesStaleEmotionStrengthAndRuntimeParameters()
    {
        var client = new CapturingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore());
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "Narration",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>
            {
                ["text"] = "雨声盖过了旁白。",
                ["emotion"] = "Calm",
                ["emotionStrength"] = "0.2",
                ["voiceReferenceId"] = IndexTts2Defaults.DefaultVoiceReferenceId,
                ["referenceText"] = "测试参考文本",
                [TtsSynthesisQueueService.PromptIdParameter] = "old_prompt",
                ["comfyOutputReference"] = "output||old.flac"
            },
            "output||old.flac");
        var project = EmptyProject() with { GenerationTasks = [task] };

        var updated = await service.ResubmitTtsTaskAsync(
            project,
            task.Id,
            new Dictionary<string, string>
            {
                ["emotion"] = "Sad",
                ["emotionStrength"] = "1.1"
            });

        var updatedTask = Assert.Single(updated.GenerationTasks);
        Assert.Equal("Sad", updatedTask.Parameters["emotion"]);
        Assert.Equal("1.1", updatedTask.Parameters["emotionStrength"]);
        Assert.Equal("prompt_tts_1", updatedTask.Parameters[TtsSynthesisQueueService.PromptIdParameter]);
        Assert.DoesNotContain("comfyOutputReference", updatedTask.Parameters.Keys);
        Assert.Equal(GenerationTaskStatus.Completed, updatedTask.Status);
        Assert.Equal("output||prompt_tts_1.flac", updatedTask.ResultAssetId);

        var submittedTask = client.SubmittedTasks.Single();
        Assert.Equal("1.1", submittedTask.Parameters["emotionStrength"]);
        Assert.DoesNotContain(TtsSynthesisQueueService.PromptIdParameter, submittedTask.Parameters.Keys);
        Assert.DoesNotContain("comfyOutputReference", submittedTask.Parameters.Keys);
    }

    [Fact]
    public async Task MarkTtsTaskForResubmissionAsync_QueuesWithoutSubmittingToComfy()
    {
        var client = new CapturingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), client, new InMemoryInteractionProjectStore());
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "Narration",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>
            {
                ["text"] = "旧文本",
                ["emotion"] = "Calm",
                ["emotionStrength"] = "0.2",
                ["voiceReferenceId"] = IndexTts2Defaults.DefaultVoiceReferenceId,
                ["referenceText"] = "测试参考文本",
                [TtsSynthesisQueueService.PromptIdParameter] = "old_prompt",
                [TtsSynthesisQueueService.QueueMessageParameter] = "old queued",
                ["comfyOutputReference"] = "output||old.flac"
            },
            "output||old.flac");
        var project = EmptyProject() with { GenerationTasks = [task] };

        var updated = await service.MarkTtsTaskForResubmissionAsync(
            project,
            task.Id,
            new Dictionary<string, string>
            {
                ["emotion"] = "Sad",
                ["emotionStrength"] = "1.1"
            });

        var updatedTask = Assert.Single(updated.GenerationTasks);
        Assert.Equal(GenerationTaskStatus.Pending, updatedTask.Status);
        Assert.Null(updatedTask.ResultAssetId);
        Assert.Equal("Sad", updatedTask.Parameters["emotion"]);
        Assert.Equal("1.1", updatedTask.Parameters["emotionStrength"]);
        Assert.DoesNotContain(TtsSynthesisQueueService.PromptIdParameter, updatedTask.Parameters.Keys);
        Assert.DoesNotContain(TtsSynthesisQueueService.QueueMessageParameter, updatedTask.Parameters.Keys);
        Assert.DoesNotContain("comfyOutputReference", updatedTask.Parameters.Keys);
        Assert.Empty(client.SubmittedTaskIds);
    }

    [Fact]
    public void SceneInteractionBuilder_TreatsPureSceneNarrationAsNarrationWithEmotion()
    {
        var project = new SceneInteractionBuilder().Build("雨声盖过了他们的呼吸。");
        var narration = project.Scenes.SelectMany(scene => scene.Interactions).Single();

        Assert.NotNull(narration.Narration);
        Assert.Null(narration.SubjectCharacterId);
        Assert.Equal("克制", narration.Emotion.Emotion);
        Assert.Equal("克制", narration.Narration.Voice.Emotion);
        Assert.InRange(narration.Narration.Voice.EmotionIntensity, 0.01, IndexTts2Defaults.MaxEmotionStrength);
    }

    [Fact]
    public void SceneInteractionBuilder_TreatsSceneSetupWithCharactersAsSceneNarration()
    {
        var project = new SceneInteractionBuilder().Build("""
林安站在雨夜街口，等了陈默三个小时。
陈默终于从街角跑来，喘着气说：对不起，我来晚了。
""");

        Assert.Contains(project.Characters, character => character.Name == "林安");
        Assert.Contains(project.Characters, character => character.Name == "陈默");
        Assert.DoesNotContain(project.Characters, character => character.Name is "于从街角" or "喘着气");

        var narration = project.Scenes
            .SelectMany(scene => scene.Interactions)
            .First(interaction => interaction.Narration?.Text.Contains("等了陈默三个小时") == true);

        Assert.Null(narration.SubjectCharacterId);
        Assert.NotNull(narration.Narration);
        Assert.Equal("克制", narration.Emotion.Emotion);
        Assert.Equal("克制", narration.Narration.Voice.Emotion);
    }

    [Fact]
    public void SceneInteractionBuilder_DoesNotInheritNarrationEmotionFromCharacterDialogue()
    {
        var project = new SceneInteractionBuilder().Build("""
陈默站在雨夜街口，低声说：我来晚了。
陈默举起了伞，雨更大了。
林安站在雨夜街口，眼眶发红：你从来没有相信我。
""");

        var chenMo = project.Characters.Single(character => character.Name == "陈默");
        var narration = project.Scenes
            .SelectMany(scene => scene.Interactions)
            .Single(interaction => interaction.Narration?.Text.Contains("举起") == true);

        Assert.NotNull(chenMo);
        Assert.Null(narration.SubjectCharacterId);
        Assert.Equal("克制", narration.Emotion.Emotion);
        Assert.Equal("克制", narration.Narration!.Voice.Emotion);
    }

    [Fact]
    public void BuildLlmInstructions_DefinesFiveInteractionKinds()
    {
        var instructions = CreateService().BuildLlmInstructions();

        Assert.Contains("type：dialogue、action、thought、effects、narration", instructions);
        Assert.Contains("dialogue：角色说出口的话", instructions);
        Assert.Contains("action：角色动作、表情或反应", instructions);
        Assert.Contains("thought：角色内心想法", instructions);
        Assert.Contains("effects：同一地点内的天气、灯光、声音、气氛变化", instructions);
        Assert.Contains("narration：旁白或剧情推进", instructions);
        Assert.DoesNotContain("relatedCharacters", instructions);
    }

    [Fact]
    public void BuildLlmInstructions_RequiresSinglePurposeRowsAndFieldBoundaries()
    {
        var instructions = CreateService().BuildLlmInstructions();

        Assert.Contains("字段说明：", instructions);
        Assert.Contains("description：当前 scene 的主要地点、环境和氛围", instructions);
        Assert.Contains("items：当前场景内按原文顺序排列的事件", instructions);
        Assert.Contains("role：dialogue、action、thought 写角色名；effects、narration 留空", instructions);
        Assert.Contains("text：当前事件的内容", instructions);
        Assert.Contains("原文主要地点变化时，新建一个 scene", instructions);
        Assert.Contains("原文明确跳到另一段时间时，新建一个 scene", instructions);
        Assert.Contains("原文切到回忆、梦境或想象时，新建一个 scene", instructions);
        Assert.DoesNotContain("写成 type=effects item", instructions);
        Assert.Contains("一条 item 只写一个事件", instructions);
        Assert.Contains("动作和台词在同一句时拆成两条", instructions);
        Assert.Contains("effects 和人物反应在同一句时拆成两条", instructions);
    }

    [Fact]
    public void BuildLlmInstructions_UsesExplicitVerifiableSceneInteractionSchema()
    {
        var instructions = CreateService().BuildLlmInstructions();

        Assert.Contains("\"scenes\"", instructions);
        Assert.Contains("\"items\"", instructions);
        Assert.Contains("\"type\": \"narration\"", instructions);
        Assert.Contains("\"type\": \"dialogue\"", instructions);
        Assert.Contains("\"type\": \"action\"", instructions);
        Assert.Contains("\"type\": \"thought\"", instructions);
        Assert.Contains("\"type\": \"effects\"", instructions);
        Assert.Contains("\"role\"", instructions);
        Assert.Contains("\"intensity\"", instructions);
        Assert.Contains("参考 JSON", instructions);
        Assert.DoesNotContain("\"character_action\"", instructions);
        Assert.DoesNotContain("\"emotionIntensity\"", instructions);
        Assert.DoesNotContain("\"sceneEffect\"", instructions);
        Assert.DoesNotContain("\"type\": \"scene\"", instructions);
    }
    private static DraftWorkspaceService CreateService()
    {
        return new DraftWorkspaceService(new MockLlmAnalysisClient(), new MockComfyUiClient(), new InMemoryInteractionProjectStore());
    }

    private static void AssertOrderedWorkflow(
        InMemoryWorkflowLogger logger,
        params (string Step, string Operation)[] expected)
    {
        var entries = logger.Entries;
        var startIndex = 0;
        foreach (var (step, operation) in expected)
        {
            var index = Array.FindIndex(entries.ToArray(), startIndex, entry => entry.Step == step && entry.Operation == operation);
            Assert.True(index >= startIndex, $"Missing workflow entry {step}/{operation} after index {startIndex}.");
            startIndex = index + 1;
        }
    }

    private static InteractionProject EmptyProject()
    {
        return new InteractionProject(
            "empty",
            "Empty",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [],
            []);
    }

    private static InteractionProject WithDefaultTtsVoices(InteractionProject project)
    {
        var updatedTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .Select(task => task with
            {
                Parameters = new Dictionary<string, string>(task.Parameters, StringComparer.Ordinal)
                {
                    ["voiceReferenceId"] = IndexTts2Defaults.DefaultVoiceReferenceId,
                    ["referenceText"] = "测试参考文本"
                }
            })
            .ToArray();

        return TtsSynthesisQueueService.ReplaceTasks(project, updatedTasks);
    }

    private sealed class CapturingComfyClient : IComfyUiClient, IComfyPromptStatusClient
    {
        public List<string> SubmittedTaskIds { get; } = [];

        public List<GenerationTask> SubmittedTasks { get; } = [];

        public List<string> StatusPromptIds { get; } = [];

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            SubmittedTaskIds.Add(request.Task.Id);
            SubmittedTasks.Add(request.Task);
            var promptId = $"prompt_{request.Task.Id}";
            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Pending,
                promptId,
                "queued",
                new ComfyPromptMetadata(promptId, 1, ComfySubmitState.Submitted, "queued")));
        }

        public Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
        {
            StatusPromptIds.Add(promptId);
            return Task.FromResult(new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Completed,
                GenerationTaskStatus.Completed,
                null,
                1,
                [$"output||{promptId}.flac"],
                "done"));
        }
    }

    private sealed class WindowedQueueComfyClient : IComfyUiClient, IComfyPromptStatusClient
    {
        public List<string> SubmittedTaskIds { get; } = [];

        public bool CompleteNextStatusCheck { get; set; }

        public bool FailNextStatusCheck { get; set; }

        public HashSet<string> NotFoundPromptIds { get; } = new(StringComparer.Ordinal);

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            SubmittedTaskIds.Add(request.Task.Id);
            var promptId = $"prompt_{request.Task.Id}";
            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Pending,
                promptId,
                "queued",
                new ComfyPromptMetadata(promptId, 1, ComfySubmitState.Submitted, "queued")));
        }

        public Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
        {
            if (NotFoundPromptIds.Contains(promptId))
            {
                return Task.FromResult(new ComfyPromptStatusResult(
                    promptId,
                    ComfyPromptState.NotFound,
                    GenerationTaskStatus.Pending,
                    null,
                    null,
                    [],
                    "not found"));
            }

            if (FailNextStatusCheck)
            {
                FailNextStatusCheck = false;
                return Task.FromResult(new ComfyPromptStatusResult(
                    promptId,
                    ComfyPromptState.Failed,
                    GenerationTaskStatus.Failed,
                    null,
                    null,
                    [],
                    "ComfyUI prompt failed: IndexTTS-2 generation failed: DefaultCPUAllocator: not enough memory"));
            }

            if (CompleteNextStatusCheck)
            {
                CompleteNextStatusCheck = false;
                return Task.FromResult(new ComfyPromptStatusResult(
                    promptId,
                    ComfyPromptState.Completed,
                    GenerationTaskStatus.Completed,
                    null,
                    1,
                    [$"output||{promptId}.flac"],
                    "done"));
            }

            return Task.FromResult(new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Queued,
                GenerationTaskStatus.Pending,
                1,
                null,
                [],
                "queued"));
        }
    }
}

