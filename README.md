# AWSTerminator
AWS Lambda that allows to stop and start the EC2 instances via Tags


List Of Commands where <ID> is integer [1 - 10]

Tag Name

Auto On <ID> - Starts the Instance, accepted value: CRON
Auto Off <ID> - Stops the Instance, accepted value: CRON
Auto Kill <ID> - Terminates the Instance, accepted valie: CRON
Auto TGR <ID> - Registers the Instance in all target groups specified by TG, accepted value: CRON
Auto TGD <ID> - Deregisters the Instance from all target groups specified by TGa, accepted value: CRON
Target Groups / TG - Defines Target Groups to register or deregister the instance from, accepted value: comma separated <TargetGroupName>:<port>

examples:

Key: TG
Value: My-Target-Group-1:80, My-Target-Group-2:3000, My-Target-Group-3:8000

Key: Target Groups
Value: My-Target-Group:80

Key: Auto On 1 
Value: 0/5 * * * * *
Explanation: Starts Instance Every 5 minutes


Cron Expression Syntax can be found here:
https://docs.aws.amazon.com/AmazonCloudWatch/latest/events/ScheduledEvents.html

Allowed Wildcards: , - * /