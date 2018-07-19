﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.NotificationSystem;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Runtime;
using Moq;
using Xunit;
using Services.Test.helpers;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.NotificationSystem.Implementation;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.NotificationSystem.Models;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Http;
using System.Net.Http.Headers;

namespace Services.Test
{
    public class NotificationSystemAgentTest
    {
        private readonly Mock<ILogger> logMock;
        private readonly Mock<IEventProcessorHostWrapper> eventProcessorHostWrapperMock;
        private readonly Mock<IEventProcessorFactory> eventProcessorFactoryMock;
        private readonly Mock<IServicesConfig> servicesConfigMock;
        private readonly Mock<IBlobStorageConfig> blobStorageConfigMock;
        private readonly Mock<INotification> notificationMock;
        private readonly Mock<IImplementation> implementationMock;
        private readonly Mock<IImplementationWrapper> implementationWrapperMock;
        private readonly Mock<IHttpRequest> httpRequestMock;
        private readonly Mock<IHttpClient> httpClientMock;


        private readonly IEventProcessor notificationEventProcessor;
        private readonly IAgent notificationSystemAgent; 

        private CancellationTokenSource agentsRunState;
        private CancellationToken runState;

        public NotificationSystemAgentTest()
        {
            this.logMock = new Mock<ILogger>();
            this.servicesConfigMock = new Mock<IServicesConfig>();
            this.notificationMock = new Mock<INotification>();
            this.implementationMock = new Mock<IImplementation>();
            this.blobStorageConfigMock = new Mock<IBlobStorageConfig>();
            this.eventProcessorFactoryMock = new Mock<IEventProcessorFactory>();
            this.eventProcessorHostWrapperMock = new Mock<IEventProcessorHostWrapper>();
            this.implementationWrapperMock = new Mock<IImplementationWrapper>();
            this.httpClientMock = new Mock<IHttpClient>();
            this.httpRequestMock = new Mock<IHttpRequest>();

            this.notificationEventProcessor = new NotificationEventProcessor(this.logMock.Object, this.servicesConfigMock.Object, this.notificationMock.Object);
            this.notificationSystemAgent = new Agent(this.logMock.Object, this.servicesConfigMock.Object, this.blobStorageConfigMock.Object, 
                                                    this.eventProcessorHostWrapperMock.Object, this.eventProcessorFactoryMock.Object);
            this.agentsRunState = new CancellationTokenSource();
        }

        [Theory, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        [InlineData(2, 2)]
        [InlineData(0, 0)]
        public void Should_CallExecuteForNTimesEqualToNumberOfJsonTokenInEventData_When_NJsonObjectsInOneEventData(int numJson, int numCalls)
        {
            // Setup
            this.notificationMock.Setup(a => a.execute()).Returns(Task.CompletedTask);
            var tempEventData = new EventData(getSamplePayLoadDataWithNalerts(numJson));

            // Act
            this.notificationEventProcessor.ProcessEventsAsync(It.IsAny<PartitionContext>(), new EventData[] { tempEventData });
            
            // Assert
            this.notificationMock.Verify(e => e.execute(), Times.Exactly(numCalls));
        }

        [Theory, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        [InlineData(2, 2)]
        [InlineData(0, 0)]
        public void Should_CallExecuteForNTimesEqualToNumberOfEventDataInMessages_When_EachEventDataHasOneJsonObject(int numEventData, int numCalls)
        { // Task.Completed Task is being passed over or Deserialize JsonObjectList is returning directly to this method.
            // Setup
            this.notificationMock.Setup(a => a.execute()).Returns(Task.CompletedTask);
            var tempEventData = new EventData(getSamplePayLoadDataWithNalerts(1));

            // Act
            this.notificationEventProcessor.ProcessEventsAsync(It.IsAny<PartitionContext>(), Enumerable.Repeat<EventData>(tempEventData, numEventData).ToArray<EventData>());

            // Assert
            this.notificationMock.Verify(e => e.execute(), Times.Exactly(numCalls));
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_RegisterEventProcessorFactory_When_CancellationTokenIsFalse()
        {
            // Arrange
            this.runState = this.agentsRunState.Token;
            this.eventProcessorHostWrapperMock.Setup(x => x.RegisterEventProcessorFactoryAsync(It.IsAny<EventProcessorHost>(), 
                It.IsAny<IEventProcessorFactory>())).Returns(Task.CompletedTask);

            // Act
            this.notificationSystemAgent.RunAsync(this.runState);

            // Assert
            this.eventProcessorHostWrapperMock.Verify(a => a.RegisterEventProcessorFactoryAsync(It.IsAny<EventProcessorHost>(), 
                It.IsAny<IEventProcessorFactory>(), It.IsAny<EventProcessorOptions>()), Times.Once);

        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_ThrowExceptionAndWriteErrorToTheLogger_When_InvalidCredentialsPassedToRegisterEventProcessorFactory()
        {
            // Arrange
            this.runState = this.agentsRunState.Token;
            this.eventProcessorHostWrapperMock.Setup(x => x.RegisterEventProcessorFactoryAsync(It.IsAny<EventProcessorHost>(), It.IsAny<IEventProcessorFactory>(), 
                It.IsAny<EventProcessorOptions>())).Returns(Task.FromException(new Exception()));

            // Act
            this.notificationSystemAgent.RunAsync(this.runState);

            // Assert
            Assert.ThrowsAsync<Exception>(() => this.eventProcessorHostWrapperMock.Object.RegisterEventProcessorFactoryAsync(It.IsAny<EventProcessorHost>(), 
                It.IsAny<IEventProcessorFactory>(), It.IsAny<EventProcessorOptions>()));
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_NotRegisterEventProcessorFactory_When_CancellationTokenIsTrue()
        {
            // Arrange
            this.agentsRunState.Cancel();
            this.runState = this.agentsRunState.Token;

            // Act
            this.notificationSystemAgent.RunAsync(this.runState);

            // Assert
            this.eventProcessorHostWrapperMock.Verify(a => a.CreateEventProcessorHost(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            this.eventProcessorHostWrapperMock.Verify(a => a.RegisterEventProcessorFactoryAsync(It.IsAny<EventProcessorHost>(), It.IsAny<IEventProcessorFactory>()), Times.Never);

        }

        [Theory, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        [InlineData(2, 2)]
        [InlineData(0, 0)]
        public void Should_ReturnAnEnumeratorOverAListOfProperJsonStrings_When_ValidInputJsonStringWithNnumberOfJsonTokens(int numJson, int numReturnJsonString)
        {
            // Setup
            var tempJson = getSampleJsonRepeatedNTimes(numJson);
            var tempNotificationEventProcessor = new NotificationEventProcessor(this.logMock.Object, this.servicesConfigMock.Object, this.notificationMock.Object);

            // Act and Assert
            Assert.Equal(tempNotificationEventProcessor.DeserializeJsonObjectList(tempJson).ToArray().Length, numReturnJsonString);
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_NotCallExecuteMethod_WhenEmptyString()
        {
            // Setup
            var tempJson = getSampleJsonRepeatedNTimes(1).Substring(0, getSampleJsonRepeatedNTimes(1).Length - 2);
            var tempNotificationEventProcessor = new NotificationEventProcessor(this.logMock.Object, this.servicesConfigMock.Object, this.notificationMock.Object);

            // Act
            tempNotificationEventProcessor.DeserializeJsonObjectList(tempJson);

            // Assert
            this.logMock.Verify(a => a.Info(It.IsAny<string>(), It.IsAny<Action>()), Times.Never);
        }

        [Theory, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        [InlineData(2, 2)]
        [InlineData(0, 0)]
        public void Should_CallExecuteMethodEqualToTheNumberOfCalls_To_TheNumberOfActionItemsInAlert(int numOfActionItems, int numOfCalls)
        {
            // Arrange
            var tempNotification = new Notification(this.implementationWrapperMock.Object)
            {
                alarm = this.getSampleAlarmWithnActions(numOfActionItems)
            };
            this.implementationWrapperMock.Setup(a => a.GetImplementationType(It.IsAny<EmailImplementationTypes>())).Returns(this.implementationMock.Object);
            this.implementationMock.Setup(a => a.execute()).Returns(Task.CompletedTask);

            // Act
            tempNotification.execute().Wait();

            // Assert
            this.implementationMock.Verify(a => a.execute(), Times.Exactly(numOfCalls));
            this.implementationMock.Verify(a => a.setMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(numOfCalls));
            this.implementationMock.Verify(a => a.setReceiver(It.IsAny<List<string>>()), Times.Exactly(numOfCalls));
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_NotCallExecuteMethod_When_AlertHasNoActions()
        {
            // Arrange
            var tempNotification = new Notification(this.implementationWrapperMock.Object)
            {
                alarm = new AlarmNotificationAsaModel()
                {
                    Actions = new List<ActionAsaModel>()
                }
            };

            // Act
            tempNotification.execute().Wait();

            // Assert
            this.implementationMock.Verify(a => a.execute(), Times.Never);
            this.implementationMock.Verify(a => a.setMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            this.implementationMock.Verify(a => a.setReceiver(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_SendPostRequestToLogicAppEndPointUrl_WhenValidEndPointUrl()
        {
            // Setup
            var tempLogicApp = new LogicApp(It.IsAny<string>(), It.IsAny<string>(),
                this.httpRequestMock.Object, this.httpClientMock.Object, this.logMock.Object);
            this.httpClientMock.Setup(x => x.PostAsync(It.IsAny<IHttpRequest>())).Returns(
                Task.FromResult<IHttpResponse>(new HttpResponse(0, It.IsAny<string>(), It.IsAny<HttpResponseHeaders>())));
            tempLogicApp.setReceiver(new List<string>() { It.IsAny<string>() });

            // Act
            tempLogicApp.execute().Wait();

            // Assert => Logger value same :( 
            this.logMock.Verify(x => x.Info("Error sending request to the LogicApp endpoiint URL", It.IsAny<Action>), Times.Once);
        }

        [Fact, Trait(Constants.TYPE, Constants.UNIT_TEST)]
        public void Should_WriteErrorToTheLogger_When_InvalidEndPointUrl()
        {
            // Setup
            var tempLogicApp = new LogicApp(It.IsAny<string>(), It.IsAny<string>(),
                this.httpRequestMock.Object, this.httpClientMock.Object, this.logMock.Object);
            tempLogicApp.setReceiver(new List<string>() { It.IsAny<string>() });
            this.httpClientMock.Setup(x => x.PostAsync(It.IsAny<IHttpRequest>())).Returns(
                Task.FromResult<IHttpResponse>(new HttpResponse(System.Net.HttpStatusCode.OK, It.IsAny<string>(), It.IsAny<HttpResponseHeaders>())));

            // Act
            tempLogicApp.execute().Wait();

            // Assert
            this.logMock.Verify(x => x.Info(It.IsAny<string>(), It.IsAny<Action>()), Times.Never);
        }


        private AlarmNotificationAsaModel getSampleAlarmWithnActions(int n)
        {
            return new AlarmNotificationAsaModel()
            {
                Rule_id = "12345",
                Rule_description = "Sample test description",
                Actions = Enumerable.Repeat(this.getSampleAction(), n).ToList<ActionAsaModel>()
            };
        }

        private ActionAsaModel getSampleAction()
        {
            return new ActionAsaModel()
            {
                ActionType = "Email",
                Parameters = new Dictionary<string, object>()
                {
                    {"Template", "Hey this is a test email" },
                    {"Subject", "Test Subject" },
                    {"Email", new Newtonsoft.Json.Linq.JArray() {"Email1@gmail.com", "Email2@gmail.com"} }
                }
            };
        }

        private static byte[] getSamplePayLoadDataWithNalerts(int n)
        {
            var dictionary = new Dictionary<string, object>()
            {
                {"created", "342874237482374" },
                {"modified", "1234123123123" },
                {"rule.description", "Pressure > 380 Aayush" },
                {"rule.severity", "Critical" },
                {"rule.id", "12345" },
                {"rule.actions", new List<object>()
                {
                    new Dictionary<string, object>(){
                    {"Type", "Email" },
                    {"Parameters", new Dictionary<string, object>(){
                        {"Template", "This is a test email."},
                        {"Email", new List<string>(){ "agupta.aayush8484@gmail.com", "t-aagupt@microsoft.com" } }
                    }
                    }
                }
                }
                },
                {"device.id", "213123" },
                {"device.msg.received", "1234123123123" }
            };
            var dictionaryList = Enumerable.Repeat<Dictionary<string, object>>(dictionary, n);
            var jsonDictionary = JsonConvert.SerializeObject(dictionaryList);

            var binaryFormatter = new BinaryFormatter();
            var memoryStream = new MemoryStream();
            binaryFormatter.Serialize(memoryStream, jsonDictionary);
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream.ToArray();
        }

        private static string getSampleJsonRepeatedNTimes(int n)
        {
            var dictionary = new Dictionary<string, object>()
            {
                {"created", "342874237482374" },
                {"modified", "1234123123123" },
                {"rule.description", "Pressure > 380 Aayush" },
                {"rule.severity", "Critical" },
                {"rule.id", "12345" },
                {"rule.actions", new List<object>()
                {
                    new Dictionary<string, object>(){
                    {"Type", "Email" },
                    {"Parameters", new Dictionary<string, object>(){
                        {"Template", "This is a test email."},
                        {"Email", new List<string>(){ "agupta.aayush8484@gmail.com", "t-aagupt@microsoft.com" } }
                    }
                    }
                }
                }
                },
                {"device.id", "213123" },
                {"device.msg.received", "1234123123123" }

            };
            var a = JsonConvert.SerializeObject(dictionary);
            return String.Join("", Enumerable.Repeat<string>(a, n).ToArray());
        }
    }  
}
