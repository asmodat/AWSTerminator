using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsmodatStandard.Extensions;
using AsmodatStandard.Extensions.Collections;
using AsmodatStandard.Extensions.Threading;
using Amazon.Lambda.Core;
using AWSWrapper.EC2;
using Amazon.EC2;
using AsmodatStandard.Types;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

/// <summary>
/// Starts, stops and termintes instances based on Auto On, Auto Off, Auto Kill tags with the use of Cron format for the value
/// https://docs.aws.amazon.com/lambda/latest/dg/tutorial-scheduled-events-schedule-expressions.html
/// </summary>
namespace AWSTerminator
{
    public class Function
    {
        private static readonly int _maxPeriodsPerInstance = 20;
        private EC2Helper _EC2;
        public Function()
        {
            _EC2 = new EC2Helper();
        }

        public async Task FunctionHandler(ILambdaContext context)
        {
            var sw = Stopwatch.StartNew();
            context.Logger.Log($"{context?.FunctionName} => {nameof(FunctionHandler)} => Started");

            try
            {
                var instances = await _EC2.ListInstances();

                if (instances.Length <= 0)
                    context.Logger.Log($"AWSTerminator can't process tags, not a single EC2 Instance was found.");

                await ParallelEx.ForEachAsync(instances, async instance => await Process(instance, context.Logger));
            }
            finally
            {
                context.Logger.Log($"{context?.FunctionName} => {nameof(FunctionHandler)} => Stopped, Eveluated within: {sw.ElapsedMilliseconds} [ms]");
            }
        }

        public async Task Process(Amazon.EC2.Model.Instance instance, ILambdaLogger logger)
        {
            if (!instance.Tags.Any(x => x.Key.Contains("Auto")))
                return;

            logger.Log($"Processing Update and Tag Validation of EC2 Instance {instance.InstanceId}, Name: {instance.GetTagValueOrDefault("Name")}");

            var disableAll = instance.GetTagValueOrDefault("Terminator Disable All").ToBoolOrDefault(false);

            if (!disableAll)
                await ParallelEx.ForAsync(0, _maxPeriodsPerInstance, async i =>
                {
                    var suffix = i == 0 ? "" : $" {i + 1}";
                    var disabled = instance.GetTagValueOrDefault($"Terminator Disable{suffix}").ToBoolOrDefault(false);

                    if (disabled)
                        return;

                    var cOn = instance.GetTagValueOrDefault($"Auto On{suffix}");
                    var cOff = instance.GetTagValueOrDefault($"Auto Off{suffix}");
                    var cKill = instance.GetTagValueOrDefault($"Auto Kill{suffix}");

                    if (cOn.IsNullOrEmpty() && cOff.IsNullOrEmpty() && cKill.IsNullOrEmpty())
                        return;

                    var ex = await ProcessTags(instance, cOn: cOn, cOff: cOff, cKill: cKill, logger: logger).CatchExceptionAsync();
                    if (ex != null)
                        logger.Log($"Failed Update or Tag Validation of EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), Auto On: '{cOn}', Auto Off: '{cOff}', Auto Kill: '{cKill}', Exception: {ex.JsonSerializeAsPrettyException()}");
                });

            logger.Log($"Finished Processing and Tag Validation of EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), DisableAll: {disableAll}");
        }

        public async Task ProcessTags(Amazon.EC2.Model.Instance instance, string cOn, string cOff, string cKill, ILambdaLogger logger)
        {
            var isRunning = instance.State.Name == InstanceStateName.Running;
            var isStopped = instance.State.Name == InstanceStateName.Stopped;
            var isTerminated = instance.State.Name == InstanceStateName.Terminated;
            var on = cOn.IsNullOrEmpty() ? -2 : cOn.ToCron().Compare(DateTime.UtcNow);
            var off = cOff.IsNullOrEmpty() ? -2 : cOff.ToCron().Compare(DateTime.UtcNow);
            var kill = cKill.IsNullOrEmpty() ? -2 : cKill.ToCron().Compare(DateTime.UtcNow);

            if (off == 0 && on == 0)
                throw new Exception("Auto On and Off are in invalid state, both are enabled, if possible set the cron not to overlap.");

            if (isRunning && (off == 0 || kill == 0))
            {
                logger.Log($"Stopping EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), Cron: {cOff}");
                var result = await _EC2.StopInstance(instanceId: instance.InstanceId, force: false);
                logger.Log($"EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), StateChange: {result.JsonSerialize(Newtonsoft.Json.Formatting.Indented)}");
                return;
            }
            else if (isStopped && (on == 0 && kill != 0))
            {
                logger.Log($"Starting EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), Cron: {cOn}");
                var result = await _EC2.StartInstance(instanceId: instance.InstanceId, additionalInfo: $"AWSTerminator Auto On, Cron: {cOn}");
                logger.Log($"EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), StateChange: {result.JsonSerialize(Newtonsoft.Json.Formatting.Indented)}");
                return;
            }
            else if (isStopped && kill == 0)
            {
                logger.Log($"Terminating EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), Cron: {cOff}");
                var result = await _EC2.TerminateInstance(instanceId: instance.InstanceId);
                logger.Log($"EC2 Instance {instance.InstanceId} ({instance.GetTagValueOrDefault("Name")}), StateChange: {result.JsonSerialize(Newtonsoft.Json.Formatting.Indented)}");
                return;
            }
        }
    }
}
