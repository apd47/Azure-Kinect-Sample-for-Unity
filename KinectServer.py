import socket
import sys
import threading

# Create a TCP/IP socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# Bind the socket to the port
server_address = ('0.0.0.0', 1935)
print(sys.stderr, 'starting up on %s port %s' % server_address)
sock.bind(server_address)

class Client(object):
    def __init__(self, role, name, address, subscription):
        self.role = role
        self.name = name
        self.address = address
        self.subscription = subscription

client_list = []

def manage_clients(ClientList):

    while True:
        print(sys.stderr, '\nwaiting to receive message')
        data, address = sock.recvfrom(65500)
        
        #print(sys.stderr, 'received %s bytes from %s' % (len(data), address))
        #print(sys.stderr, data)
        
        if data:
            split = data.decode().split("|")
            #print(split[0])
            if (split[0] == "JOIN"):
                # Add the new client to the client dictionary (Should replace none with something else by default...)
                ClientList.append(Client(split[1], split[2], address, 'none'))

                # Confirm to the new client that they're joined
                message = "CONFIRM|JOIN|" + split[1] + "|" + split[2]+ "|"
                sock.sendto(message.encode(), address)
                print (sys.stderr, 'sent %s bytes back to %s' % (message, address))

                # Notify all clients of the new user
                message = "NOTICE|JOIN|" + split[1] + "|" + split[2]+ "|"
                for client in ClientList:
                    sock.sendto(message.encode(), client.address)
                    #print (sys.stderr, 'sent %s bytes back to %s' % (message, client.address))

            elif (split[0] == "LEAVE"):
                # Remove the client from the client dictionary
                for client in ClientList:
                    if(client.role == split[1] and client.name == split[2] and client.address == address):
                        ClientList.remove(client)
                
                # Confirm to the client that they're removed
                message = "CONFIRM|LEAVE|" + split[1] + "|" + split[2]+ "|"
                sock.sendto(message.encode(), address)
                print (sys.stderr, 'sent %s bytes back to %s' % (message, address))

                # Notify all clients about the removed client
                message = "NOTICE|LEAVE|" + split[1] + "|" + split[2]+ "|"
                for client in ClientList:
                    sock.sendto(message.encode(), client.address)
                    #print (sys.stderr, 'sent %s bytes back to %s' % (message, client.address))

            elif (split[0] == "SUBSCRIBE"):
                # Find the client in the client dictionary and set their subscription status
                for client in ClientList:
                    if(client.name == split[2] and client.address == address):
                        client.subscription = split[1]
                
                # Confirm to the client that they're subscribed
                message = "CONFIRM|SUBSCRIBE|" + split[1] + "|" + split[2]+ "|"
                sock.sendto(message.encode(), address)
                print (sys.stderr, 'sent %s bytes back to %s' % (message, address))

                # Notify all clients about the removed client
                message = "NOTICE|SUBSCRIBE|" + split[1] + "|" + split[2]+ "|"
                for client in ClientList:
                    sock.sendto(message.encode(), client.address)
                    #print (sys.stderr, 'sent %s bytes back to %s' % (message, client.address))

            elif (split[0] == "PROVIDE"):
                # Verify that the source client and address is a registered provider
                for client in ClientList:
                    if(client.name == split[1] and client.address == address):
                        
                        # Confirm to the client that the frame was recieved
                        message = "CONFIRM|PROVIDE|" + split[1] + "|" + split[2]+ "|"
                        sock.sendto(message.encode(), address)
                        print (sys.stderr, 'sent %s bytes back to %s' % (message, address))

                        # Forward the frame to all recipients subscribed to the provider
                        message = "NOTICE|PROVIDE|" + split[1] + "|" + split[2]+ "|" + split[3]+ "|"
                        for client in ClientList:
                            if(client.subscription == split[1]):
                                sock.sendto(message.encode(), client.address)
                                #print (sys.stderr, 'sent %s bytes back to %s' % (message, client.address))


t1 = threading.Thread(target=manage_clients, args=(client_list,))

t1.start()
t1.join()


